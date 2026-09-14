using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter.Sequencer;

// Deserialization compatibility for saved sequences. Removed from the item catalog in 1.4.
[JsonObject(MemberSerialization.OptIn)]
public sealed class QsmSmartRecoveryGate : SequenceItem {
    public QsmSmartRecoveryGate() { }
    public string LastAction => "RETIRED — CONTINUE CAPTURING";
    public string LastDetail => "Smart Recovery was removed in QSM 1.4. This legacy item does nothing and can be removed from the sequence.";
    public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) => Task.CompletedTask;
    public override object Clone() => new QsmSmartRecoveryGate { Name = Name, Category = Category, Description = Description, Icon = Icon };
}
