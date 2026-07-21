using System;
using System.Threading.Tasks;

namespace LanhuRuntimeSync.EditorTools
{
    internal static class LanhuRuntimeSyncService
    {
        public static bool IsBusy { get; private set; }
        public static string LastReport { get; private set; }

        public static async Task<LanhuImportReport> ImportOrUpdateAsync(
            LanhuSourceReference source,
            LanhuDesignInfo design,
            string cookie,
            LanhuImportOptions options)
        {
            return await RunAsync(() => LanhuPrefabSynchronizer.ImportOrUpdateAsync(source, design, cookie, options));
        }

        public static async Task<LanhuImportReport> UpdateRootAsync(LanhuRuntimeSyncRoot root, string cookie)
        {
            return await RunAsync(() => LanhuPrefabSynchronizer.UpdateRootAsync(root, cookie));
        }

        private static async Task<LanhuImportReport> RunAsync(Func<Task<LanhuImportReport>> operation)
        {
            if (IsBusy)
            {
                throw new InvalidOperationException("Lanhu Runtime Sync is already running.");
            }

            IsBusy = true;
            LastReport = "Lanhu Runtime Sync started.";
            try
            {
                var report = await operation();
                LastReport = report.ToString();
                return report;
            }
            catch (Exception exception)
            {
                LastReport = $"Lanhu Runtime Sync failed: {exception.Message}";
                throw;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
