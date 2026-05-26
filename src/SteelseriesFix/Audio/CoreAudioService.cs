using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteelseriesFix.Audio;

public sealed class CoreAudioService : IAudioService
{
    private const int DeviceStateActive = 0x00000001;
    private const int DeviceStateAll = 0x0000000F;
    private const int StgmRead = 0;
    private const uint ClsCtxAll = 0x17;
    private static readonly Guid MmDeviceEnumeratorId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid AudioSessionManager2Id = typeof(IAudioSessionManager2).GUID;
    private static readonly Guid VolumeChangeEventContext = new("7B4A1E80-B2AF-48D5-8120-56636151BFAC");

    public IReadOnlyList<AudioEndpoint> GetEndpoints(AudioEndpointKind kind)
    {
        var activeEndpoints = ReadEndpoints(kind, DeviceStateActive);
        return activeEndpoints.Count > 0 ? activeEndpoints : ReadEndpoints(kind, DeviceStateAll);
    }

    private static IReadOnlyList<AudioEndpoint> ReadEndpoints(AudioEndpointKind kind, int stateMask)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;

        try
        {
            enumerator = CreateDeviceEnumerator();
            ThrowIfFailed(
                enumerator.EnumAudioEndpoints(ToDataFlow(kind), stateMask, out collection),
                "Enumerating audio endpoints");

            ThrowIfFailed(collection.GetCount(out var count), "Counting audio endpoints");

            var endpoints = new List<AudioEndpoint>((int)count);
            for (var index = 0u; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    ThrowIfFailed(collection.Item(index, out device), "Reading audio endpoint");
                    ThrowIfFailed(device.GetId(out var id), "Reading audio endpoint ID");

                    var displayName = TryGetFriendlyName(device, id);
                    endpoints.Add(new AudioEndpoint(id, displayName, kind));
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }

            return endpoints
                .OrderBy(endpoint => endpoint.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        finally
        {
            ReleaseComObject(collection);
            ReleaseComObject(enumerator);
        }
    }

    public EndpointMuteResult SetDiscordVolumeToZero(AudioEndpoint endpoint, IReadOnlyCollection<string> targetProcessNames)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;
        var deviceFound = false;
        var matchedSessions = 0;
        var updatedSessions = 0;
        var errors = new List<string>();

        try
        {
            enumerator = CreateDeviceEnumerator();
            var getDeviceResult = enumerator.GetDevice(endpoint.Id, out device);
            if (Failed(getDeviceResult))
            {
                return EndpointMuteResult.MissingDevice(endpoint);
            }

            deviceFound = true;

            var sessionManagerId = AudioSessionManager2Id;
            var activateResult = device.Activate(ref sessionManagerId, ClsCtxAll, IntPtr.Zero, out var sessionManagerObject);
            if (Failed(activateResult) || sessionManagerObject is null)
            {
                return new EndpointMuteResult(
                    endpoint.Kind,
                    endpoint.Id,
                    endpoint.DisplayName,
                    true,
                    0,
                    0,
                    $"Could not open audio sessions ({FormatHResult(activateResult)}).");
            }

            sessionManager = (IAudioSessionManager2)sessionManagerObject;
            var enumerateResult = sessionManager.GetSessionEnumerator(out sessionEnumerator);
            if (Failed(enumerateResult) || sessionEnumerator is null)
            {
                return new EndpointMuteResult(
                    endpoint.Kind,
                    endpoint.Id,
                    endpoint.DisplayName,
                    true,
                    0,
                    0,
                    $"Could not enumerate audio sessions ({FormatHResult(enumerateResult)}).");
            }

            ThrowIfFailed(sessionEnumerator.GetCount(out var sessionCount), "Counting audio sessions");

            for (var index = 0; index < sessionCount; index++)
            {
                IAudioSessionControl? sessionControl = null;
                try
                {
                    var getSessionResult = sessionEnumerator.GetSession(index, out sessionControl);
                    if (Failed(getSessionResult) || sessionControl is not IAudioSessionControl2 sessionControl2)
                    {
                        continue;
                    }

                    var processIdResult = sessionControl2.GetProcessId(out var processId);
                    if (Failed(processIdResult))
                    {
                        continue;
                    }

                    var processName = TryGetProcessName(processId);
                    if (!DiscordProcessMatcher.IsTargetProcess(processName, targetProcessNames))
                    {
                        continue;
                    }

                    matchedSessions++;

                    if (sessionControl is not ISimpleAudioVolume simpleAudioVolume)
                    {
                        errors.Add("The Discord session did not expose a volume control.");
                        continue;
                    }

                    var eventContext = VolumeChangeEventContext;
                    var setVolumeResult = simpleAudioVolume.SetMasterVolume(0.0f, ref eventContext);
                    if (Failed(setVolumeResult))
                    {
                        errors.Add($"Setting Discord volume failed ({FormatHResult(setVolumeResult)}).");
                        continue;
                    }

                    updatedSessions++;
                }
                finally
                {
                    ReleaseComObject(sessionControl);
                }
            }

            return new EndpointMuteResult(
                endpoint.Kind,
                endpoint.Id,
                endpoint.DisplayName,
                true,
                matchedSessions,
                updatedSessions,
                errors.Count == 0 ? null : string.Join(" ", errors));
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
        {
            return new EndpointMuteResult(
                endpoint.Kind,
                endpoint.Id,
                endpoint.DisplayName,
                deviceFound,
                matchedSessions,
                updatedSessions,
                ex.Message);
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);
            ReleaseComObject(sessionManager);
            ReleaseComObject(device);
            ReleaseComObject(enumerator);
        }
    }

    public IReadOnlyList<AudioSessionInfo> GetSessions(AudioEndpoint endpoint, IReadOnlyCollection<string> targetProcessNames)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;

        try
        {
            enumerator = CreateDeviceEnumerator();
            ThrowIfFailed(enumerator.GetDevice(endpoint.Id, out device), "Opening audio endpoint");

            var sessionManagerId = AudioSessionManager2Id;
            ThrowIfFailed(
                device.Activate(ref sessionManagerId, ClsCtxAll, IntPtr.Zero, out var sessionManagerObject),
                "Opening audio session manager");

            sessionManager = (IAudioSessionManager2)sessionManagerObject!;
            ThrowIfFailed(sessionManager.GetSessionEnumerator(out sessionEnumerator), "Enumerating audio sessions");
            ThrowIfFailed(sessionEnumerator.GetCount(out var sessionCount), "Counting audio sessions");

            var sessions = new List<AudioSessionInfo>(sessionCount);
            for (var index = 0; index < sessionCount; index++)
            {
                IAudioSessionControl? sessionControl = null;

                try
                {
                    if (Failed(sessionEnumerator.GetSession(index, out sessionControl)))
                    {
                        continue;
                    }

                    var processId = TryGetSessionProcessId(sessionControl);
                    var processName = TryGetProcessName(processId);
                    var displayName = TryGetSessionDisplayName(sessionControl);
                    var masterVolume = TryGetSessionMasterVolume(sessionControl);

                    sessions.Add(new AudioSessionInfo(
                        endpoint.Id,
                        endpoint.DisplayName,
                        endpoint.Kind,
                        processId,
                        processName,
                        displayName,
                        masterVolume,
                        DiscordProcessMatcher.IsTargetProcess(processName, targetProcessNames)));
                }
                finally
                {
                    ReleaseComObject(sessionControl);
                }
            }

            return sessions;
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);
            ReleaseComObject(sessionManager);
            ReleaseComObject(device);
            ReleaseComObject(enumerator);
        }
    }

    private static EDataFlow ToDataFlow(AudioEndpointKind kind) =>
        kind == AudioEndpointKind.Playback ? EDataFlow.Render : EDataFlow.Capture;

    private static IMMDeviceEnumerator CreateDeviceEnumerator()
    {
        var enumeratorType = Type.GetTypeFromCLSID(MmDeviceEnumeratorId, throwOnError: true)!;
        return (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
    }

    private static string TryGetFriendlyName(IMMDevice device, string fallback)
    {
        IPropertyStore? propertyStore = null;
        var propertyValue = default(PropVariant);
        var propertyValueInitialized = false;

        try
        {
            ThrowIfFailed(device.OpenPropertyStore(StgmRead, out propertyStore), "Opening audio endpoint properties");

            var propertyKey = PropertyKeys.DeviceFriendlyName;
            ThrowIfFailed(propertyStore.GetValue(ref propertyKey, out propertyValue), "Reading audio endpoint friendly name");
            propertyValueInitialized = true;

            var displayName = propertyValue.GetString();
            return string.IsNullOrWhiteSpace(displayName) ? fallback : displayName;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return fallback;
        }
        finally
        {
            if (propertyValueInitialized)
            {
                propertyValue.Clear();
            }

            ReleaseComObject(propertyStore);
        }
    }

    private static string? TryGetProcessName(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static uint TryGetSessionProcessId(IAudioSessionControl sessionControl)
    {
        if (sessionControl is not IAudioSessionControl2 sessionControl2)
        {
            return 0;
        }

        return Failed(sessionControl2.GetProcessId(out var processId)) ? 0 : processId;
    }

    private static string? TryGetSessionDisplayName(IAudioSessionControl sessionControl)
    {
        var result = sessionControl.GetDisplayName(out var displayName);
        return Failed(result) || string.IsNullOrWhiteSpace(displayName) ? null : displayName;
    }

    private static float? TryGetSessionMasterVolume(IAudioSessionControl sessionControl)
    {
        if (sessionControl is not ISimpleAudioVolume simpleAudioVolume)
        {
            return null;
        }

        return Failed(simpleAudioVolume.GetMasterVolume(out var level)) ? null : level;
    }

    private static bool Failed(int hresult) => hresult < 0;

    private static string FormatHResult(int hresult) => $"0x{unchecked((uint)hresult):X8}";

    private static void ThrowIfFailed(int hresult, string operation)
    {
        if (!Failed(hresult))
        {
            return;
        }

        var exception = Marshal.GetExceptionForHR(hresult);
        throw new InvalidOperationException($"{operation} failed ({FormatHResult(hresult)}).", exception);
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }

    private static class PropertyKeys
    {
        public static PropertyKey DeviceFriendlyName => new(
            new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
            14);
    }

    private enum EDataFlow
    {
        Render = 0,
        Capture = 1,
        All = 2
    }

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2
    }

    private enum AudioSessionState
    {
        Inactive = 0,
        Active = 1,
        Expired = 2
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, uint classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? interfaceObject);

        [PreserveSig]
        int OpenPropertyStore(int accessMode, out IPropertyStore properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint properties);

        [PreserveSig]
        int GetAt(uint propertyIndex, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);

        [PreserveSig]
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;

        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        private const ushort VtLpwstr = 31;
        private ushort _valueType;
        private ushort _reserved1;
        private ushort _reserved2;
        private ushort _reserved3;
        private IntPtr _value;

        public readonly string? GetString()
        {
            return _valueType == VtLpwstr && _value != IntPtr.Zero
                ? Marshal.PtrToStringUni(_value)
                : null;
        }

        public void Clear()
        {
            PropVariantClear(ref this);
        }

        [DllImport("Ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant propVariant);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(ref Guid audioSessionGuid, uint streamFlags, out IAudioSessionControl sessionControl);

        [PreserveSig]
        int GetSimpleAudioVolume(ref Guid audioSessionGuid, uint streamFlags, out ISimpleAudioVolume audioVolume);

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);

        [PreserveSig]
        int RegisterSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int UnregisterSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);

        [PreserveSig]
        int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);

        [PreserveSig]
        int GetSession(int sessionIndex, out IAudioSessionControl sessionControl);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out AudioSessionState state);

        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

        [PreserveSig]
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);

        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

        [PreserveSig]
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid groupingId);

        [PreserveSig]
        int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr notifications);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr notifications);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig]
        int GetState(out AudioSessionState state);

        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

        [PreserveSig]
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);

        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

        [PreserveSig]
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid groupingId);

        [PreserveSig]
        int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr notifications);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr notifications);

        [PreserveSig]
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionId);

        [PreserveSig]
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceId);

        [PreserveSig]
        int GetProcessId(out uint processId);

        [PreserveSig]
        int IsSystemSoundsSession();

        [PreserveSig]
        int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig]
        int SetMasterVolume(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolume(out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
