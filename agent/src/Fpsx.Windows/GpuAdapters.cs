using System.Runtime.InteropServices;
using Fpsx.Core.Benchmark;

namespace Fpsx.Windows;

/// <summary>
/// Placas de vídeo do PC pelo DXGI, na ordem de "alto desempenho" que o
/// próprio Windows usa (Configurações > Tela > Gráficos). O papel de cada uma
/// (integrada, dedicada, virtual) vem dos sinais do driver; o nome só desempata.
/// Qualquer falha devolve lista vazia, e o status da partida vira "não foi
/// possível determinar": nunca um palpite.
/// </summary>
public static class GpuAdapters
{
    private const uint AdapterFlagRemote = 1;
    private const uint AdapterFlagSoftware = 2;
    private const uint MicrosoftVendor = 0x1414;
    private const int HighPerformancePreference = 2;
    private const int NotFound = unchecked((int)0x887A0002);

    public static IReadOnlyList<GpuAdapter> List()
    {
        try
        {
            return Enumerate();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or DllNotFoundException or EntryPointNotFoundException)
        {
            return [];
        }
    }

    private static List<GpuAdapter> Enumerate()
    {
        var iid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref iid, out var raw) != 0 || raw is not IDXGIFactory1 factory)
            return [];
        var descs = new List<(AdapterDesc1 Desc, int? Order)>();
        try
        {
            if (factory is IDXGIFactory6 f6)
            {
                // Ordem do Windows: a primeira é a que ele usa para "Alto desempenho".
                var adapterIid = typeof(IDXGIAdapter1).GUID;
                for (uint i = 0; f6.EnumAdapterByGpuPreference(i, HighPerformancePreference, ref adapterIid, out var a) == 0; i++)
                    descs.Add((Describe(a), (int)i));
            }
            else
            {
                // Windows sem a lista de preferência: sem ordem, decide pela memória dedicada.
                for (uint i = 0; factory.EnumAdapters1(i, out var a) != NotFound && a is not null; i++)
                    descs.Add((Describe(a), null));
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }

        return descs
            .GroupBy(d => Key(d.Desc.Luid))
            .Select(g => g.OrderBy(d => d.Order ?? int.MaxValue).First())
            .Select(d => ToAdapter(d.Desc, d.Order))
            .ToList();
    }

    private static AdapterDesc1 Describe(object adapter)
    {
        try
        {
            var a = (IDXGIAdapter1)adapter;
            return a.GetDesc1(out var d) == 0 ? d : default;
        }
        finally
        {
            Marshal.ReleaseComObject(adapter);
        }
    }

    private static GpuAdapter ToAdapter(AdapterDesc1 d, int? order)
    {
        var name = (d.Description ?? "").Trim();
        var mb = (long)(d.DedicatedVideoMemory.ToUInt64() / (1024 * 1024));
        var (hybridIntegrated, hybridDiscrete, virtualDevice) = KmtType(d.Luid);
        var software = (d.Flags & AdapterFlagSoftware) != 0 || d.VendorId == MicrosoftVendor;
        var remote = (d.Flags & AdapterFlagRemote) != 0 || virtualDevice;
        var role = GpuSelector.Classify(software, remote, hybridIntegrated, hybridDiscrete, mb,
            GpuSelector.NameLooksIntegrated(SnapshotCollector.VendorOfName(VendorName(d.VendorId) + " " + name), name));
        return new GpuAdapter(Key(d.Luid), name, role, mb, order);
    }

    private static string VendorName(uint id) => id switch
    {
        0x10DE => "NVIDIA",
        0x1002 or 0x1022 => "AMD",
        0x8086 => "Intel",
        _ => "",
    };

    /// <summary>Mesma chave do contador de GPU do Windows (<see cref="CounterMath.LuidKey"/>).</summary>
    private static string Key(Luid l) => $"{l.High:X8}{l.Low:X8}";

    /// <summary>
    /// Marcação do driver (D3DKMT_ADAPTERTYPE): notebook híbrido informa qual
    /// é a integrada e qual é a dedicada, e placa de máquina virtual se
    /// identifica. null = driver não respondeu.
    /// </summary>
    private static (bool? HybridIntegrated, bool? HybridDiscrete, bool Virtual) KmtType(Luid luid)
    {
        try
        {
            var open = new OpenAdapterFromLuid { Luid = luid };
            if (D3DKMTOpenAdapterFromLuid(ref open) != 0)
                return (null, null, false);
            var buffer = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(buffer, 0);
                var q = new QueryAdapterInfo { Adapter = open.Adapter, Type = AdapterTypeQuery, Data = buffer, DataSize = 4 };
                if (D3DKMTQueryAdapterInfo(ref q) != 0)
                    return (null, null, false);
                var flags = (uint)Marshal.ReadInt32(buffer);
                // Bits: 4 HybridDiscrete, 5 HybridIntegrated, 6 IndirectDisplayDevice, 7 Paravirtualized.
                return ((flags & (1 << 5)) != 0, (flags & (1 << 4)) != 0, (flags & ((1 << 6) | (1 << 7))) != 0);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                var close = new CloseAdapter { Adapter = open.Adapter };
                D3DKMTCloseAdapter(ref close);
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return (null, null, false);
        }
    }

    private const int AdapterTypeQuery = 15;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        public uint Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public Luid Luid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromLuid
    {
        public Luid Luid;
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter
    {
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public IntPtr Data;
        public uint DataSize;
    }

    // Interfaces COM do DXGI declaradas planas: os métodos que o RKZFPS não
    // usa ficam como marcadores, só para manter a posição de cada um na vtable.
#pragma warning disable IDE1006, SA1300
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void _SetPrivateData();
        void _SetPrivateDataInterface();
        void _GetPrivateData();
        void _GetParent();
        void _EnumAdapters();
        void _MakeWindowAssociation();
        void _GetWindowAssociation();
        void _CreateSwapChain();
        void _CreateSoftwareAdapter();

        [PreserveSig]
        int EnumAdapters1(uint index, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter1? adapter);
    }

    [ComImport, Guid("c1b6694f-ff09-44a9-b03c-77900a0a1d17"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory6
    {
        void _SetPrivateData();
        void _SetPrivateDataInterface();
        void _GetPrivateData();
        void _GetParent();
        void _EnumAdapters();
        void _MakeWindowAssociation();
        void _GetWindowAssociation();
        void _CreateSwapChain();
        void _CreateSoftwareAdapter();
        void _EnumAdapters1();
        void _IsCurrent();
        void _IsWindowedStereoEnabled();
        void _CreateSwapChainForHwnd();
        void _CreateSwapChainForCoreWindow();
        void _GetSharedResourceAdapterLuid();
        void _RegisterStereoStatusWindow();
        void _RegisterStereoStatusEvent();
        void _UnregisterStereoStatus();
        void _RegisterOcclusionStatusWindow();
        void _RegisterOcclusionStatusEvent();
        void _UnregisterOcclusionStatus();
        void _CreateSwapChainForComposition();
        void _GetCreationFlags();
        void _EnumAdapterByLuid();
        void _EnumWarpAdapter();
        void _CheckFeatureSupport();

        [PreserveSig]
        int EnumAdapterByGpuPreference(uint index, int preference, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object adapter);
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void _SetPrivateData();
        void _SetPrivateDataInterface();
        void _GetPrivateData();
        void _GetParent();
        void _EnumOutputs();
        void _GetDesc();
        void _CheckInterfaceSupport();

        [PreserveSig]
        int GetDesc1(out AdapterDesc1 desc);
    }
#pragma warning restore IDE1006, SA1300

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapterFromLuid open);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo query);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter close);
}
