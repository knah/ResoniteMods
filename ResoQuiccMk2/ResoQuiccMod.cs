using System.Net.Quic;
using System.Reflection;
using System.Runtime.InteropServices;
using Elements.Core;
using FrooxEngine;
using ResoniteModLoader;

namespace ResoQuiccMk2;

public class ResoQuiccMod : CommonModBase<ResoQuiccMod>
{
    public override string Name => "ResoQuicc";
    
    // There's no NAT punch, so this is only useful on exposed servers (headless?)
    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> EnableDirectQuicHost = new("Enable direct QUIC hosting", "QUIC direct hosting", () => false);
    
    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> EnableDirectQuicConnect = new("Enable direct QUIC connections", "QUIC direct connections", () => true);
    
    // This is primarily expected to be useful for sessions hosted via relay
    [AutoRegisterConfigKey] public static readonly ModConfigurationKey<bool> FetchExternalIpForHost =
        new("Fetch external IP for QUIC hosting",
            "If enabled, QUIC connection will advertise your public API as determined using a web request",
            () => false);
    
    [AutoRegisterConfigKey] public static readonly ModConfigurationKey<string> BindIP =
        new("Bind IP", "The IP address to bind to", () => "0.0.0.0");
    
    [AutoRegisterConfigKey] public static readonly ModConfigurationKey<string> AdvertiseIPOverride =
        new("Advertise IP override", "The IP[:port] to advertise as a public address. Empty mean auto-detect. If you omit port, actual bound port will be used. It's up to you to ensure your host is reachable on this address.", () => "");

    private static QuicDirectNetworkManager? _directNetworkManager;
    
    public override void OnEngineInit()
    {
        NativeLibrary.SetDllImportResolver(typeof(QuicListener).Assembly, ImportResolver);
        UniLog.Log($"Quic Base dir: {Environment.CurrentDirectory}");
        if (OperatingSystem.IsLinux())
        {
            // With proton, lib loading works in mysterious ways
            // These are not in runtime path, so expect the user to copy them by hand or something
            NativeLibrary.Load(Path.Combine(Environment.CurrentDirectory, "libnl-3.so.200"));
            NativeLibrary.Load(Path.Combine(Environment.CurrentDirectory, "libnl-route-3.so.200"));
            NativeLibrary.Load(Path.Combine(Environment.CurrentDirectory, "libnuma.so.1"));
            NativeLibrary.Load(Path.Combine(Environment.CurrentDirectory, "libcrypto.so.3"));
            NativeLibrary.Load(Path.Combine(Environment.CurrentDirectory, "libbpf.so.1"));
            NativeLibrary.Load(Path.Combine(Environment.CurrentDirectory, "libxdp.so.1"));
            NativeLibrary.Load(Path.Combine(Environment.CurrentDirectory, "libmsquic.so"));
        }

        if (!QuicListener.IsSupported)
        {
            UniLog.Error("Quic is not supported - check that you're on the correct platform!");
            return;
        }
        
        _directNetworkManager = new QuicDirectNetworkManager();
        
        Engine.Current.RunPostInit(() =>
        {
            Engine.Current.NetworkManager.RegisterNetworkManager(_directNetworkManager);
        });
    }

    private IntPtr ImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        UniLog.Log($"QUIC import resolver started, lib {libraryName}");
        if (libraryName != "msquic" && libraryName != "libmsquic.so" && libraryName != "msquic.dll")
            return 0;
        
        var libName = OperatingSystem.IsLinux() ? "libmsquic.so" : "msquic.dll";
        var simpleLoad = NativeLibrary.Load(libraryName);
        if (simpleLoad != 0) 
            return simpleLoad;

        var libsRoot = Environment.CurrentDirectory;
        var maybeLibPath = Path.Combine(libsRoot, "rml_libs", libName);
        UniLog.Log($"Looking for msquic at {maybeLibPath}");
        if (NativeLibrary.TryLoad(maybeLibPath, out var maybeLib))
            return maybeLib;
        
        return NativeLibrary.Load(Path.Combine(libsRoot, libName));
    }
}