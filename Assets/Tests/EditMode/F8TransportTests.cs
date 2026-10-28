using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Client.Framework.Release;
using NUnit.Framework;

namespace Client.Tests.EditMode
{
    [Category("F8")]
    public sealed class F8TransportTests
    {
        [Test] public void ObservationPreservesSuccessfulBytesAndOwnedDisposal()
        {
            string file=Path.GetTempFileName();var inner=new Transfer();var observed=new ObservedReleaseTransport(inner);
            try { observed.DownloadAsync("channel.json",file,CancellationToken.None).GetAwaiter().GetResult(); Assert.That(File.ReadAllText(file),Is.EqualTo("actual"));Assert.That(observed.Records.Count,Is.EqualTo(1));Assert.That(observed.Records[0].Success,Is.True);Assert.That(observed.Records[0].Bytes,Is.EqualTo(6));observed.Dispose();Assert.That(inner.Disposed,Is.True); }
            finally { File.Delete(file); }
        }
        [Test] public void ObservationRetainsOriginalFailureAndDoesNotTurnItIntoSuccess()
        {
            string file=Path.GetTempFileName();var error=new IOException("transport unavailable");var observed=new ObservedReleaseTransport(new Transfer{Failure=error});
            try { Assert.That(Assert.Throws<IOException>(()=>observed.DownloadAsync("channel.json",file,CancellationToken.None).GetAwaiter().GetResult()),Is.SameAs(error));Assert.That(observed.Records[0].Success,Is.False);Assert.That(observed.Records[0].Bytes,Is.Zero);Assert.That(observed.Records[0].Error,Does.Contain("transport unavailable")); }
            finally { observed.Dispose();File.Delete(file); }
        }
        private sealed class Transfer:IReleaseTransport,IDisposable
        { public Exception Failure;public bool Disposed;public Task DownloadAsync(string path,string destination,CancellationToken token){token.ThrowIfCancellationRequested();if(Failure!=null)throw Failure;File.WriteAllText(destination,"actual");return Task.CompletedTask;}public void Dispose(){Disposed=true;} }
    }
}
