using System.Management;

namespace SystemCleaner.Services
{
    /// <summary>
    /// Створює точку відновлення системи через WMI перед очищенням.
    /// </summary>
    public class RestorePointCreator
    {
        public async Task<bool> CreateAsync(string description, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var scope = new ManagementScope(@"\\localhost\root\default");
                    scope.Connect();

                    using var systemRestore = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
                    var inParams = systemRestore.GetMethodParameters("CreateRestorePoint");
                    inParams["Description"] = description;
                    inParams["RestorePointType"] = 0; // APPLICATION_INSTALL
                    inParams["EventType"] = 100; // BEGIN_SYSTEM_CHANGE

                    var outParams = systemRestore.InvokeMethod("CreateRestorePoint", inParams, null);
                    var retVal = Convert.ToInt32(outParams?["ReturnValue"] ?? -1);
                    return retVal == 0;
                }
                catch
                {
                    return false;
                }
            }, ct);
        }
    }
}