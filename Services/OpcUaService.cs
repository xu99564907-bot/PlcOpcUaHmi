using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class OpcUaService
{
    private Session? _session;
    private ApplicationConfiguration? _configuration;
    private Subscription? _subscription;
    private readonly Dictionary<string, MonitoredItem> _monitoredItems = new();

    public bool IsConnected => _session?.Connected == true;
    public string ConnectionStatus => IsConnected ? "已连接" : "未连接";
    public event Action<string, string>? TagValueChanged;

    public async Task ConnectAsync(OpcUaConnectionOptions options)
    {
        _configuration = await BuildConfigurationAsync();
        await DisconnectAsync();

        var endpointUrls = BuildCandidateEndpointUrls(options);
        Exception? lastException = null;

        foreach (var endpointUrl in endpointUrls)
        {
            foreach (var useSecurity in new[] { false, true })
            {
                try
                {
                    _session = await CreateSessionAsync(_configuration, endpointUrl, useSecurity, options);
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }
        }

        throw new InvalidOperationException(
            $"无法连接到 OPC UA 服务器：{options.GetEndpointUrl()}。请检查 Endpoint、匿名权限或安全策略设置。{Environment.NewLine}{lastException?.Message}",
            lastException);
    }

    public async Task DisconnectAsync()
    {
        await UnsubscribeAllAsync();
        if (_session is null)
        {
            return;
        }

        await _session.CloseAsync();
        _session.Dispose();
        _session = null;
    }

    public async Task<Dictionary<string, string>> ReadTagsAsync(IEnumerable<TagItem> tags)
    {
        if (_session is null || !_session.Connected)
        {
            return tags.ToDictionary(t => t.Name, _ => "未连接");
        }

        var result = new Dictionary<string, string>();
        foreach (var tag in tags)
        {
            try
            {
                var value = await _session.ReadValueAsync(NodeId.Parse(tag.NodeId));
                result[tag.Name] = value.Value?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                result[tag.Name] = $"ERR: {ex.Message}";
            }
        }

        return result;
    }

    public async Task<(string Value, string DataType, string StatusCode, string Timestamp)> ReadNodeAsync(string nodeId)
    {
        if (_session is null || !_session.Connected)
        {
            throw new InvalidOperationException("OPC UA 未连接。");
        }

        var dataValue = await _session.ReadValueAsync(NodeId.Parse(nodeId));
        var dataType = dataValue.WrappedValue.TypeInfo?.BuiltInType.ToString() ?? dataValue.Value?.GetType().Name ?? "--";
        var timestamp = dataValue.SourceTimestamp == DateTime.MinValue
            ? "--"
            : dataValue.SourceTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        return (
            dataValue.Value?.ToString() ?? string.Empty,
            dataType,
            dataValue.StatusCode.ToString(),
            timestamp);
    }

    public Task<IReadOnlyList<OpcUaBrowseNode>> BrowseNodeAsync(string? nodeId = null)
    {
        if (_session is null || !_session.Connected)
        {
            throw new InvalidOperationException("OPC UA 未连接。");
        }

        var targetNodeId = string.IsNullOrWhiteSpace(nodeId) ? ObjectIds.ObjectsFolder : NodeId.Parse(nodeId);
        var browser = new Browser(_session)
        {
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = (int)(NodeClass.Object | NodeClass.Variable | NodeClass.Method | NodeClass.View)
        };

        var references = browser.Browse(targetNodeId);
        var nodes = references
            .Select(reference =>
            {
                var resolvedNodeId = ExpandedNodeId.ToNodeId(reference.NodeId, _session.NamespaceUris);
                var node = new OpcUaBrowseNode
                {
                    DisplayName = reference.DisplayName.Text ?? reference.BrowseName.Name ?? resolvedNodeId?.ToString() ?? "(Unnamed)",
                    NodeId = resolvedNodeId?.ToString() ?? string.Empty,
                    NodeClass = reference.NodeClass.ToString(),
                    HasChildren = reference.NodeClass is NodeClass.Object or NodeClass.Variable or NodeClass.View
                };

                if (node.HasChildren)
                {
                    node.Children.Add(OpcUaBrowseNode.CreatePlaceholder());
                }

                return node;
            })
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeId))
            .OrderBy(node => node.NodeClass)
            .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<OpcUaBrowseNode>>(nodes);
    }

    public async Task WriteTagAsync(TagItem tag, object value)
    {
        if (_session is null || !_session.Connected)
        {
            throw new InvalidOperationException("OPC UA 未连接。");
        }

        var writeValue = new WriteValue
        {
            NodeId = NodeId.Parse(tag.NodeId),
            AttributeId = Attributes.Value,
            Value = new DataValue(new Variant(value))
        };

        var collection = new WriteValueCollection { writeValue };
        var response = await _session.WriteAsync(null, collection, default);
        ClientBase.ValidateResponse(response.Results, collection);
        ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, collection);
    }

    public async Task SubscribeTagsAsync(IEnumerable<TagItem> tags, int publishingInterval = 500)
    {
        if (_session is null || !_session.Connected)
        {
            return;
        }

        await UnsubscribeAllAsync();

        _subscription = new Subscription(_session.DefaultSubscription)
        {
            PublishingInterval = publishingInterval,
            DisplayName = "PlcOpcUaHmiSubscription"
        };

        _session.AddSubscription(_subscription);
        await _subscription.CreateAsync(CancellationToken.None);

        foreach (var tag in tags.Where(t => !string.IsNullOrWhiteSpace(t.NodeId)))
        {
            var monitoredItem = new MonitoredItem(_subscription.DefaultItem)
            {
                DisplayName = tag.Name,
                StartNodeId = NodeId.Parse(tag.NodeId),
                AttributeId = Attributes.Value,
                SamplingInterval = publishingInterval,
                QueueSize = 1,
                DiscardOldest = true
            };

            monitoredItem.Notification += (_, _) =>
            {
                foreach (var value in monitoredItem.DequeueValues())
                {
                    TagValueChanged?.Invoke(tag.Name, value.Value?.ToString() ?? string.Empty);
                }
            };

            _subscription.AddItem(monitoredItem);
            _monitoredItems[tag.Name] = monitoredItem;
        }

        await _subscription.ApplyChangesAsync(CancellationToken.None);
    }

    public async Task UnsubscribeAllAsync()
    {
        if (_subscription is not null && _session is not null)
        {
            try
            {
                await _subscription.DeleteAsync(true, CancellationToken.None);
                await _session.RemoveSubscriptionAsync(_subscription, CancellationToken.None);
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }

        _monitoredItems.Clear();
        _subscription = null;
    }

    private static async Task<ApplicationConfiguration> BuildConfigurationAsync()
    {
        var appRoot = ResolveApplicationRoot();
        var pkiRoot = Path.Combine(appRoot, "config", "pki");
        var ownStore = Path.Combine(pkiRoot, "own");
        var trustedStore = Path.Combine(pkiRoot, "trusted");
        var issuerStore = Path.Combine(pkiRoot, "issuer");
        var rejectedStore = Path.Combine(pkiRoot, "rejected");

        Directory.CreateDirectory(ownStore);
        Directory.CreateDirectory(trustedStore);
        Directory.CreateDirectory(issuerStore);
        Directory.CreateDirectory(rejectedStore);

        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "PlcOpcUaHmi",
            ApplicationUri = $"urn:{Utils.GetHostName()}:PlcOpcUaHmi",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = ownStore,
                    SubjectName = "CN=PlcOpcUaHmi, O=OpenAI, C=CN"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = trustedStore
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = issuerStore
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = rejectedStore
                },
                AutoAcceptUntrustedCertificates = true,
                RejectSHA1SignedCertificates = false,
                MinimumCertificateKeySize = 1024
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
            TraceConfiguration = new TraceConfiguration()
        };

        await configuration.ValidateAsync(ApplicationType.Client);
        configuration.CertificateValidator.CertificateValidation += (_, e) => { e.Accept = true; };
        return configuration;
    }

    private static string ResolveApplicationRoot()
    {
        var currentDirectory = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(currentDirectory, "PlcOpcUaHmi.csproj")))
        {
            return currentDirectory;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PlcOpcUaHmi.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static async Task<Session> CreateSessionAsync(
        ApplicationConfiguration configuration,
        string endpointUrl,
        bool useSecurity,
        OpcUaConnectionOptions options)
    {
        var selectedEndpoint = await CoreClientUtils.SelectEndpointAsync(
            configuration,
            endpointUrl,
            useSecurity,
            15000,
            null!,
            CancellationToken.None);

        var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, EndpointConfiguration.Create(configuration));
        IUserIdentity userIdentity = options.UseAnonymous
            ? new UserIdentity(new AnonymousIdentityToken())
            : new UserIdentity(options.Username, Encoding.UTF8.GetBytes(options.Password));

        var sessionFactory = new DefaultSessionFactory(null!);
        return (Session)await sessionFactory.CreateAsync(
            configuration,
            endpoint,
            false,
            false,
            "PlcOpcUaHmi",
            60000,
            userIdentity,
            null,
            CancellationToken.None);
    }

    private static IReadOnlyList<string> BuildCandidateEndpointUrls(OpcUaConnectionOptions options)
    {
        var urls = new List<string>();
        var baseUrl = options.GetEndpointUrl();
        urls.Add(baseUrl);

        if (string.IsNullOrWhiteSpace(options.EndpointPath))
        {
            var trailingSlashUrl = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/";
            if (!urls.Contains(trailingSlashUrl, StringComparer.OrdinalIgnoreCase))
            {
                urls.Add(trailingSlashUrl);
            }

            var discoveryUrl = baseUrl.EndsWith("/", StringComparison.OrdinalIgnoreCase)
                ? baseUrl + "discovery"
                : baseUrl + "/discovery";
            if (!urls.Contains(discoveryUrl, StringComparer.OrdinalIgnoreCase))
            {
                urls.Add(discoveryUrl);
            }
        }

        return urls;
    }
}
