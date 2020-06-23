using System.IO;
using System.Security.Cryptography;

namespace ANYWAYS.VectorTiles.CycleNetworks
{
    /// <summary>
    /// Contains helper methods for hash creation and checking.
    /// </summary>
    public static class HashHelper
    {
        /// <summary>
        /// Checks if the a file's content and it's hash match.
        /// </summary>
        public static bool CheckMatch(string file, string hashFile)
        {
            // calculate MD5.
            byte[] newHash = null;
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(file))
                {
                    newHash = md5.ComputeHash(stream);
                }
            }

            // load existing.
            if (!File.Exists(hashFile)) return false;
            var existingHash = File.ReadAllBytes(hashFile);

            return CompareHash(newHash, existingHash);
        }

        /// <summary>
        /// Compares two hashes, returns false if different, true otherwise.
        /// </summary>
        public static bool CompareHash(byte[] hash1, byte[] hash2)
        {
            // compare.
            if (hash1.Length != hash2.Length)
            {
                return false;
            }
            for (var i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Writes a hash file.
        /// </summary>
        public static void WriteHash(string file)
        {
            WriteHash(file, file + ".hash");
        }

        /// <summary>
        /// Writes a hash file.
        /// </summary>
        public static void WriteHash(string file, string hashFile)
        {
            // calculate MD5.
            byte[] newHash = null;
            using (var md5 = MD5.Create())
            {
                using var stream = File.OpenRead(file);
                newHash = md5.ComputeHash(stream);
            }

            var directory = new FileInfo(hashFile).DirectoryName;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(hashFile))
            {
                File.Delete(hashFile);
            }
            File.WriteAllBytes(hashFile, newHash);
        }
    }
}