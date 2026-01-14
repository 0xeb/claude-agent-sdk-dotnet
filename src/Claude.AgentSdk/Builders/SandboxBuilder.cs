namespace Claude.AgentSdk.Builders;

/// <summary>
/// Builder for configuring sandbox settings.
/// </summary>
public sealed class SandboxBuilder
{
    private bool? _enabled;
    private bool? _autoAllowBashIfSandboxed;
    private readonly List<string> _excludedCommands = [];
    private bool? _allowUnsandboxedCommands;
    private SandboxNetworkConfig? _network;
    private SandboxIgnoreViolations? _ignoreViolations;
    private bool? _enableWeakerNestedSandbox;

    /// <summary>Enable the sandbox.</summary>
    public SandboxBuilder Enable()
    {
        _enabled = true;
        return this;
    }

    /// <summary>Disable the sandbox.</summary>
    public SandboxBuilder Disable()
    {
        _enabled = false;
        return this;
    }

    /// <summary>Auto-allow bash if sandboxed.</summary>
    public SandboxBuilder AutoAllowBash(bool value = true)
    {
        _autoAllowBashIfSandboxed = value;
        return this;
    }

    /// <summary>Exclude commands from sandbox.</summary>
    public SandboxBuilder ExcludeCommands(params string[] commands)
    {
        _excludedCommands.AddRange(commands);
        return this;
    }

    /// <summary>Allow unsandboxed commands.</summary>
    public SandboxBuilder AllowUnsandboxedCommands(bool value = true)
    {
        _allowUnsandboxedCommands = value;
        return this;
    }

    /// <summary>Configure network settings.</summary>
    public SandboxBuilder Network(Action<NetworkBuilder> configure)
    {
        var builder = new NetworkBuilder();
        configure(builder);
        _network = builder.Build();
        return this;
    }

    /// <summary>Configure violations to ignore.</summary>
    public SandboxBuilder IgnoreViolations(Action<ViolationsBuilder> configure)
    {
        var builder = new ViolationsBuilder();
        configure(builder);
        _ignoreViolations = builder.Build();
        return this;
    }

    /// <summary>Enable weaker nested sandbox.</summary>
    public SandboxBuilder EnableWeakerNestedSandbox(bool value = true)
    {
        _enableWeakerNestedSandbox = value;
        return this;
    }

    internal SandboxSettings Build()
    {
        return new SandboxSettings
        {
            Enabled = _enabled,
            AutoAllowBashIfSandboxed = _autoAllowBashIfSandboxed,
            ExcludedCommands = _excludedCommands.Count > 0 ? _excludedCommands : null,
            AllowUnsandboxedCommands = _allowUnsandboxedCommands,
            Network = _network,
            IgnoreViolations = _ignoreViolations,
            EnableWeakerNestedSandbox = _enableWeakerNestedSandbox
        };
    }

    /// <summary>Builder for network configuration.</summary>
    public sealed class NetworkBuilder
    {
        private readonly List<string> _allowUnixSockets = [];
        private bool? _allowAllUnixSockets;
        private bool? _allowLocalBinding;
        private int? _httpProxyPort;
        private int? _socksProxyPort;

        /// <summary>Allow specific unix sockets.</summary>
        public NetworkBuilder AllowUnixSockets(params string[] sockets)
        {
            _allowUnixSockets.AddRange(sockets);
            return this;
        }

        /// <summary>Allow all unix sockets.</summary>
        public NetworkBuilder AllowAllUnixSockets(bool value = true)
        {
            _allowAllUnixSockets = value;
            return this;
        }

        /// <summary>Allow local binding.</summary>
        public NetworkBuilder AllowLocalBinding(bool value = true)
        {
            _allowLocalBinding = value;
            return this;
        }

        /// <summary>Set HTTP proxy port.</summary>
        public NetworkBuilder HttpProxyPort(int port)
        {
            _httpProxyPort = port;
            return this;
        }

        /// <summary>Set SOCKS proxy port.</summary>
        public NetworkBuilder SocksProxyPort(int port)
        {
            _socksProxyPort = port;
            return this;
        }

        internal SandboxNetworkConfig Build()
        {
            return new SandboxNetworkConfig
            {
                AllowUnixSockets = _allowUnixSockets.Count > 0 ? _allowUnixSockets : null,
                AllowAllUnixSockets = _allowAllUnixSockets,
                AllowLocalBinding = _allowLocalBinding,
                HttpProxyPort = _httpProxyPort,
                SocksProxyPort = _socksProxyPort
            };
        }
    }

    /// <summary>Builder for violations to ignore.</summary>
    public sealed class ViolationsBuilder
    {
        private readonly List<string> _file = [];
        private readonly List<string> _network = [];

        /// <summary>Ignore file violations.</summary>
        public ViolationsBuilder File(params string[] patterns)
        {
            _file.AddRange(patterns);
            return this;
        }

        /// <summary>Ignore network violations.</summary>
        public ViolationsBuilder Network(params string[] patterns)
        {
            _network.AddRange(patterns);
            return this;
        }

        internal SandboxIgnoreViolations Build()
        {
            return new SandboxIgnoreViolations
            {
                File = _file.Count > 0 ? _file : null,
                Network = _network.Count > 0 ? _network : null
            };
        }
    }
}
