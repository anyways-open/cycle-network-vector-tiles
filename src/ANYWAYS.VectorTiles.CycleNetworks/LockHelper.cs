using System;
using System.IO;
using Newtonsoft.Json;
using Serilog;

namespace ANYWAYS.VectorTiles.CycleNetworks
{
    public static class LockHelper
    {
        /// <summary>
        /// Returns true if the lock file exists and is still valid.
        /// </summary>
        public static bool IsLocked(string lockFile)
        {
            var syncLockInfo = new FileInfo(lockFile);
            if (syncLockInfo.Exists)
            {
                try
                {
                    var lockData = JsonConvert.DeserializeObject<Lock>(File.ReadAllText(syncLockInfo.FullName));
                    var timeStamp = new DateTime(lockData.Time);
                    if ((DateTime.Now - timeStamp).TotalHours > 1)
                    {
                        Log.Information("Lock indicates a failed update, deleting lockfile and free things for retry: {0}.",
                            lockFile);
                        syncLockInfo.Delete();

                        return false;
                    }
                    else
                    { // file is there and still valid.
                        return true;
                    }
                }
                catch(Exception ex)
                {
                    Log.Error("Error in parsing lock file, assuming no lock: {0} - {1}", syncLockInfo.FullName,
                        ex.ToString());
                }
            }

            return false;
        }
        
        /// <summary>
        /// Writes the lock file.
        /// </summary>
        public static void WriteLock(string lockFile)
        {
            try
            {
                var syncLockInfo = new FileInfo(lockFile);
                if (!syncLockInfo.Directory.Exists)
                {
                    syncLockInfo.Directory.Create();
                }

                // write lock file (the triggered process should not know about the content of the lock file, only delete it when done).
                var l = new Lock()
                {
                    Time = DateTime.Now.Ticks
                };
                File.WriteAllText(syncLockInfo.FullName, 
                    JsonConvert.SerializeObject(l));
            }
            catch(Exception ex)
            {
                Log.Error("Error in parsing lock file, assuming no lock: {0} - {1}", lockFile,
                    ex.ToString());
            }
        }
    }
}