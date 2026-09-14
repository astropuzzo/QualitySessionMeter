using System.Reflection;
using System.Runtime.InteropServices;

[assembly: Guid("bf861692-b3de-4fdc-8a74-d2b97434f49d")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.3.0.1057")]
[assembly: AssemblyMetadata("License", "Apache-2.0")]
[assembly: AssemblyMetadata("LicenseURL", "https://www.apache.org/licenses/LICENSE-2.0")]
[assembly: AssemblyMetadata("Repository", "https://github.com/astropuzzo/QualitySessionMeter")]
[assembly: AssemblyMetadata("Homepage", "https://github.com/astropuzzo/QualitySessionMeter")]
[assembly: AssemblyMetadata("Tags", "Quality,Subframe,Guiding,Clouds,Session,Imaging,Monitoring")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/astropuzzo/QualitySessionMeter/releases")]
[assembly: AssemblyMetadata("LongDescription", "QualitySessionMeter evaluates completed LIGHT frames using exposure-specific guiding and rolling same-context star-count/background baselines. An optional stellar second pass verifies guide-reject candidates using raw central-star eccentricity, tails and repeated secondary peaks, with configurable tolerances and persistent visual proof. It can rescue moderate guide false positives before file handling while retaining severe guiding and independent signal rejects. It provides last-frame Quality, diagnostic evidence strength, session reports, Valid Frame Target, and a read-only local Web Dashboard. Smart Recovery waits are retired. Rejected images are never deleted by the normal QSM workflow. The Web Dashboard is disabled by default, exposes no remote control endpoints, and does not configure Internet or router access.")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://raw.githubusercontent.com/astropuzzo/QualitySessionMeter/main/docs/store/featured.png")]
[assembly: AssemblyMetadata("ScreenshotURL", "https://raw.githubusercontent.com/astropuzzo/QualitySessionMeter/main/docs/store/nina-panel.png")]
[assembly: AssemblyMetadata("AltScreenshotURL", "https://raw.githubusercontent.com/astropuzzo/QualitySessionMeter/main/docs/store/web-dashboard.png")]
[assembly: ComVisible(false)]
