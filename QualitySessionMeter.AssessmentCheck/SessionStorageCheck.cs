using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;
using System.IO;

internal static class SessionStorageCheck {
    public static async Task Run(Action<bool,string> check) {
        string root=Path.Combine(Path.GetTempPath(),"qsm-storage-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string fallbackSession=null;
        void RemoveOwned(string path,string parent) {
            if(string.IsNullOrEmpty(path)||!Directory.Exists(path))return;
            if(!Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Cleanup outside test root");
            Directory.Delete(path,true);
        }
        try {
            string night=Path.Combine(root,"night"),image=Path.Combine(night,"LIGHT","HOO","001.fits");
            check(SessionPathResolver.Resolve(2,"",image)==Path.Combine(night,"QSM"),"reports can follow a nested LIGHT/filter directory");
            check(SessionPathResolver.Resolve(2,"",Path.Combine(night,"002.fits"))==Path.Combine(night,"QSM"),"images without a LIGHT parent use their own directory");
            var accessor=new InMemoryPluginOptionsAccessor();var options=new QualitySettings(accessor);
            check(options.SessionStorageModeIndex==0,"existing profiles keep local reports by default");
            options.SessionStorageModeIndex=1;options.SessionOutputDirectory=night;
            check(new QualitySettings(accessor).SessionOutputDirectory==night && new QualitySettings(accessor).SessionStorageModeIndex==1,"session destination persists in plugin settings");
            string destination=night;
            var store=new SessionStore(directoryResolver:r=>SessionPathResolver.Resolve(1,destination,r.OriginalPath));
            FrameQualityResult Frame(int n)=>new(){FrameIndex=n,TimestampUtc=DateTime.UtcNow,OriginalPath=image,Status=FrameStatus.Accepted,OverallQuality=100};
            await store.AppendAsync(Frame(1));string first=store.SessionFolder;
            check(File.Exists(Path.Combine(first,"session.json")) && first.StartsWith(Path.Combine(night,"QSM")),"custom destination receives session JSON and artifacts");
            destination=Path.Combine(root,"next");await store.AppendAsync(Frame(2));
            check(store.SessionFolder==first && File.ReadAllLines(Path.Combine(first,"frames.csv")).Length==3,"changing destination does not split the active session");
            store.Reset();await store.AppendAsync(Frame(3));
            check(store.SessionFolder!=first && store.SessionFolder.StartsWith(destination),"next session uses the changed destination");
            var fallback=new SessionStore(directoryResolver:r=>throw new UnauthorizedAccessException("Test destination unavailable"));
            await fallback.AppendAsync(Frame(4));fallbackSession=fallback.SessionFolder;
            check(fallback.StorageWarning.Contains("default local") && File.Exists(Path.Combine(fallbackSession,"session.json")) && fallback.Results.Single().Status==FrameStatus.Accepted,"unwritable destination falls back visibly without changing the verdict");
            var invalid=new SessionStore(directoryResolver:r=>SessionPathResolver.Resolve(1,"relative-path",r.OriginalPath));
            await invalid.AppendAsync(Frame(5));
            check(invalid.StorageWarning.Length>0,"relative custom paths cannot silently redirect reports");
            RemoveOwned(invalid.SessionFolder,SessionPathResolver.DefaultDirectory);
        } finally {
            RemoveOwned(fallbackSession,SessionPathResolver.DefaultDirectory);
            RemoveOwned(root,Path.GetTempPath());
        }
    }
}
