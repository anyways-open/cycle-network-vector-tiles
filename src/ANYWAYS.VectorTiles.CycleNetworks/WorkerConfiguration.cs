namespace ANYWAYS.VectorTiles.CycleNetworks
{
    public class WorkerConfiguration
    {
        public string SourceUrl { get; set; } = null!;

        public string DataPath { get; set; } = null!;
        
        public string TargetPath { get; set; } = null!;

        public int RefreshTime { get; set; } = 1000;
        
        public string TempPath { get; set; } = null!;
    }
}