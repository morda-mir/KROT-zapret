using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace KROT.Service;

internal enum ServiceRegistrationAction
{
    Install,
    Stop,
    Uninstall
}

internal static class ServiceRegistration
{
    private const string ServiceName = "KROTZapret";
    private const string DisplayName = "KROT zapret Service";
    private const string Description = "Local runtime broker for KROT zapret.";
    private const string ServiceSddl =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)" +
        "(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)" +
        "(A;;CCLCSWRPWPDTLOCRRC;;;IU)" +
        "(A;;CCLCSWLOCRRC;;;SU)";
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    public static int Run(ServiceRegistrationAction action)
    {
        try
        {
            switch (action)
            {
                case ServiceRegistrationAction.Install:
                    Install();
                    break;
                case ServiceRegistrationAction.Stop:
                    Stop();
                    break;
                case ServiceRegistrationAction.Uninstall:
                    Uninstall();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }

            return 0;
        }
        catch (Exception ex)
        {
            TryWriteFailure(action, ex);
            return ex is TimeoutException ? 1460 : 1;
        }
    }

    private static void Install()
    {
        var serviceDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        var serviceExecutable = Path.Combine(serviceDirectory, "KROT.Service.exe");
        var runtimeManifest = Path.Combine(
            serviceDirectory,
            "runtime",
            "runtime-manifest.json");
        if (!File.Exists(serviceExecutable))
        {
            throw new FileNotFoundException(
                "KROT service executable is missing.",
                serviceExecutable);
        }

        if (!File.Exists(runtimeManifest))
        {
            throw new FileNotFoundException(
                "KROT runtime manifest is missing.",
                runtimeManifest);
        }

        SecureDirectoryTree(serviceDirectory);
        var stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "KROT zapret",
            "service-state");
        Directory.CreateDirectory(stateDirectory);
        SecureDirectoryTree(stateDirectory);

        using var serviceManager = OpenServiceManager(
            NativeMethods.ScManagerConnect | NativeMethods.ScManagerCreateService);
        using var existingService = TryOpenService(
            serviceManager,
            NativeMethods.ServiceAllAccess);
        var binaryPath = $"\"{serviceExecutable}\" --real-runtime";
        SafeServiceHandle service;
        if (existingService != null)
        {
            StopAndWait(existingService);
            if (!NativeMethods.ChangeServiceConfig(
                    existingService,
                    NativeMethods.ServiceWin32OwnProcess,
                    NativeMethods.ServiceDemandStart,
                    NativeMethods.ServiceErrorNormal,
                    binaryPath,
                    null,
                    IntPtr.Zero,
                    null,
                    "LocalSystem",
                    null,
                    DisplayName))
            {
                throw LastWin32Exception("Failed to update the KROT service.");
            }

            service = existingService;
        }
        else
        {
            service = NativeMethods.CreateService(
                serviceManager,
                ServiceName,
                DisplayName,
                NativeMethods.ServiceAllAccess,
                NativeMethods.ServiceWin32OwnProcess,
                NativeMethods.ServiceDemandStart,
                NativeMethods.ServiceErrorNormal,
                binaryPath,
                null,
                IntPtr.Zero,
                null,
                null,
                null);
            if (service.IsInvalid)
            {
                service.Dispose();
                throw LastWin32Exception("Failed to create the KROT service.");
            }
        }

        if (!ReferenceEquals(service, existingService))
        {
            using (service)
            {
                ConfigureService(service);
            }
        }
        else
        {
            ConfigureService(service);
        }
    }

    private static void Stop()
    {
        using var serviceManager = OpenServiceManager(NativeMethods.ScManagerConnect);
        using var service = TryOpenService(
            serviceManager,
            NativeMethods.ServiceStop | NativeMethods.ServiceQueryStatus);
        if (service != null)
        {
            StopAndWait(service);
        }
    }

    private static void Uninstall()
    {
        using var serviceManager = OpenServiceManager(NativeMethods.ScManagerConnect);
        using var service = TryOpenService(
            serviceManager,
            NativeMethods.ServiceStop |
            NativeMethods.ServiceQueryStatus |
            NativeMethods.Delete);
        if (service == null)
        {
            return;
        }

        StopAndWait(service);
        if (!NativeMethods.DeleteService(service))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NativeMethods.ErrorServiceMarkedForDelete)
            {
                throw new Win32Exception(error, "Failed to delete the KROT service.");
            }
        }

        service.Dispose();
        var deadline = DateTime.UtcNow + StopTimeout;
        while (DateTime.UtcNow < deadline)
        {
            using var remaining = TryOpenService(
                serviceManager,
                NativeMethods.ServiceQueryStatus);
            if (remaining == null)
            {
                return;
            }

            Thread.Sleep(200);
        }

        throw new TimeoutException("Timed out while deleting the KROT service.");
    }

    private static void ConfigureService(SafeServiceHandle service)
    {
        var description = new NativeMethods.ServiceDescription
        {
            Description = Description
        };
        if (!NativeMethods.ChangeServiceConfig2(
                service,
                NativeMethods.ServiceConfigDescription,
                ref description))
        {
            throw LastWin32Exception("Failed to set the KROT service description.");
        }

        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(
                ServiceSddl,
                NativeMethods.SddlRevision1,
                out var securityDescriptor,
                IntPtr.Zero))
        {
            throw LastWin32Exception("Failed to prepare the KROT service permissions.");
        }

        try
        {
            if (!NativeMethods.SetServiceObjectSecurity(
                    service,
                    NativeMethods.DaclSecurityInformation,
                    securityDescriptor))
            {
                throw LastWin32Exception("Failed to configure the KROT service permissions.");
            }
        }
        finally
        {
            NativeMethods.LocalFree(securityDescriptor);
        }
    }

    private static SafeServiceHandle OpenServiceManager(uint access)
    {
        var serviceManager = NativeMethods.OpenSCManager(null, null, access);
        if (serviceManager.IsInvalid)
        {
            serviceManager.Dispose();
            throw LastWin32Exception("Failed to open the Windows Service Manager.");
        }

        return serviceManager;
    }

    private static SafeServiceHandle? TryOpenService(
        SafeServiceHandle serviceManager,
        uint access)
    {
        var service = NativeMethods.OpenService(serviceManager, ServiceName, access);
        if (!service.IsInvalid)
        {
            return service;
        }

        var error = Marshal.GetLastWin32Error();
        service.Dispose();
        if (error == NativeMethods.ErrorServiceDoesNotExist)
        {
            return null;
        }

        throw new Win32Exception(error, "Failed to open the KROT service.");
    }

    private static void StopAndWait(SafeServiceHandle service)
    {
        var status = QueryStatus(service);
        if (status.CurrentState == NativeMethods.ServiceStopped)
        {
            return;
        }

        if (status.CurrentState != NativeMethods.ServiceStopPending &&
            !NativeMethods.ControlService(
                service,
                NativeMethods.ServiceControlStop,
                out _))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NativeMethods.ErrorServiceNotActive)
            {
                throw new Win32Exception(error, "Failed to stop the KROT service.");
            }

            return;
        }

        var deadline = DateTime.UtcNow + StopTimeout;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(200);
            if (QueryStatus(service).CurrentState == NativeMethods.ServiceStopped)
            {
                return;
            }
        }

        throw new TimeoutException("Timed out while stopping the KROT service.");
    }

    private static NativeMethods.ServiceStatusProcess QueryStatus(SafeServiceHandle service)
    {
        var status = new NativeMethods.ServiceStatusProcess();
        if (!NativeMethods.QueryServiceStatusEx(
                service,
                NativeMethods.ScStatusProcessInfo,
                ref status,
                Marshal.SizeOf(status),
                out _))
        {
            throw LastWin32Exception("Failed to query the KROT service status.");
        }

        return status;
    }

    private static void SecureDirectoryTree(string root)
    {
        ApplyDirectorySecurity(root);
        foreach (var directory in Directory.EnumerateDirectories(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
            {
                ApplyDirectorySecurity(directory);
            }
        }

        foreach (var file in Directory.EnumerateFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            ApplyFileSecurity(file);
        }
    }

    private static void ApplyDirectorySecurity(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddDirectoryRule(
            security,
            WellKnownSidType.LocalSystemSid,
            FileSystemRights.FullControl);
        AddDirectoryRule(
            security,
            WellKnownSidType.BuiltinAdministratorsSid,
            FileSystemRights.FullControl);
        AddDirectoryRule(
            security,
            WellKnownSidType.BuiltinUsersSid,
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize);
        Directory.SetAccessControl(path, security);
    }

    private static void ApplyFileSecurity(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddFileRule(
            security,
            WellKnownSidType.LocalSystemSid,
            FileSystemRights.FullControl);
        AddFileRule(
            security,
            WellKnownSidType.BuiltinAdministratorsSid,
            FileSystemRights.FullControl);
        AddFileRule(
            security,
            WellKnownSidType.BuiltinUsersSid,
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize);
        File.SetAccessControl(path, security);
    }

    private static void AddDirectoryRule(
        FileSystemSecurity security,
        WellKnownSidType sidType,
        FileSystemRights rights)
    {
        security.AddAccessRule(
            new FileSystemAccessRule(
                new SecurityIdentifier(sidType, null),
                rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
    }

    private static void AddFileRule(
        FileSystemSecurity security,
        WellKnownSidType sidType,
        FileSystemRights rights)
    {
        security.AddAccessRule(
            new FileSystemAccessRule(
                new SecurityIdentifier(sidType, null),
                rights,
                AccessControlType.Allow));
    }

    private static Win32Exception LastWin32Exception(string message) =>
        new(Marshal.GetLastWin32Error(), message);

    private static void TryWriteFailure(
        ServiceRegistrationAction action,
        Exception exception)
    {
        try
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "KROT-service-registration.log");
            File.AppendAllText(
                path,
                $"{DateTime.UtcNow:O} {action}: {exception}{Environment.NewLine}");
        }
        catch
        {
            // Installation must return the original error code even if diagnostics fail.
        }
    }

    private sealed class SafeServiceHandle : SafeHandle
    {
        private SafeServiceHandle()
            : base(IntPtr.Zero, ownsHandle: true)
        {
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle() =>
            NativeMethods.CloseServiceHandle(handle);
    }

    private static class NativeMethods
    {
        internal const uint ScManagerConnect = 0x0001;
        internal const uint ScManagerCreateService = 0x0002;
        internal const uint ServiceQueryConfig = 0x0001;
        internal const uint ServiceChangeConfig = 0x0002;
        internal const uint ServiceQueryStatus = 0x0004;
        internal const uint ServiceEnumerateDependents = 0x0008;
        internal const uint ServiceStart = 0x0010;
        internal const uint ServiceStop = 0x0020;
        internal const uint ServicePauseContinue = 0x0040;
        internal const uint ServiceInterrogate = 0x0080;
        internal const uint ServiceUserDefinedControl = 0x0100;
        internal const uint Delete = 0x00010000;
        internal const uint StandardRightsRequired = 0x000F0000;
        internal const uint ServiceAllAccess =
            StandardRightsRequired |
            ServiceQueryConfig |
            ServiceChangeConfig |
            ServiceQueryStatus |
            ServiceEnumerateDependents |
            ServiceStart |
            ServiceStop |
            ServicePauseContinue |
            ServiceInterrogate |
            ServiceUserDefinedControl;
        internal const uint ServiceWin32OwnProcess = 0x00000010;
        internal const uint ServiceDemandStart = 0x00000003;
        internal const uint ServiceErrorNormal = 0x00000001;
        internal const uint ServiceControlStop = 0x00000001;
        internal const uint ServiceStopped = 0x00000001;
        internal const uint ServiceStopPending = 0x00000003;
        internal const int ScStatusProcessInfo = 0;
        internal const int ServiceConfigDescription = 1;
        internal const uint DaclSecurityInformation = 0x00000004;
        internal const uint SddlRevision1 = 1;
        internal const int ErrorServiceDoesNotExist = 1060;
        internal const int ErrorServiceNotActive = 1062;
        internal const int ErrorServiceMarkedForDelete = 1072;

        [StructLayout(LayoutKind.Sequential)]
        internal struct ServiceStatus
        {
            internal uint ServiceType;
            internal uint CurrentState;
            internal uint ControlsAccepted;
            internal uint Win32ExitCode;
            internal uint ServiceSpecificExitCode;
            internal uint CheckPoint;
            internal uint WaitHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ServiceStatusProcess
        {
            internal uint ServiceType;
            internal uint CurrentState;
            internal uint ControlsAccepted;
            internal uint Win32ExitCode;
            internal uint ServiceSpecificExitCode;
            internal uint CheckPoint;
            internal uint WaitHint;
            internal uint ProcessId;
            internal uint ServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ServiceDescription
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            internal string Description;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeServiceHandle OpenSCManager(
            string? machineName,
            string? databaseName,
            uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeServiceHandle OpenService(
            SafeServiceHandle serviceManager,
            string serviceName,
            uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeServiceHandle CreateService(
            SafeServiceHandle serviceManager,
            string serviceName,
            string displayName,
            uint desiredAccess,
            uint serviceType,
            uint startType,
            uint errorControl,
            string binaryPathName,
            string? loadOrderGroup,
            IntPtr tagId,
            string? dependencies,
            string? serviceStartName,
            string? password);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ChangeServiceConfig(
            SafeServiceHandle service,
            uint serviceType,
            uint startType,
            uint errorControl,
            string binaryPathName,
            string? loadOrderGroup,
            IntPtr tagId,
            string? dependencies,
            string? serviceStartName,
            string? password,
            string displayName);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ChangeServiceConfig2(
            SafeServiceHandle service,
            int infoLevel,
            ref ServiceDescription info);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ControlService(
            SafeServiceHandle service,
            uint control,
            out ServiceStatus status);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryServiceStatusEx(
            SafeServiceHandle service,
            int infoLevel,
            ref ServiceStatusProcess buffer,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteService(SafeServiceHandle service);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetServiceObjectSecurity(
            SafeServiceHandle service,
            uint securityInformation,
            IntPtr securityDescriptor);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
            string stringSecurityDescriptor,
            uint stringSdRevision,
            out IntPtr securityDescriptor,
            IntPtr securityDescriptorSize);

        [DllImport("advapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseServiceHandle(IntPtr serviceHandle);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}
