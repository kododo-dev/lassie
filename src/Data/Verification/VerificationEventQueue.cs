using System.Threading.Channels;
using Lassie.Data.Licenses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lassie.Data.Verification;

// The seam between the verify request thread (producer) and the background writer
// (single consumer). The producer side is non-blocking and never throws: if the buffer
// is full the event is dropped and counted, so a burst of traffic can never add latency
// to — or fail — a verification response.
public interface IVerificationEventQueue
{
    void Enqueue(LicenseVerificationEvent evt);
    ChannelReader<LicenseVerificationEvent> Reader { get; }
}

public sealed class VerificationEventQueue : IVerificationEventQueue
{
    private readonly Channel<LicenseVerificationEvent> _channel;
    private readonly ILogger<VerificationEventQueue> _log;
    private long _dropped;

    public VerificationEventQueue(IConfiguration config, ILogger<VerificationEventQueue> log)
    {
        _log = log;
        var capacity = config.GetValue("Verification:QueueCapacity", 10_000);
        _channel = Channel.CreateBounded<LicenseVerificationEvent>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            });
    }

    public ChannelReader<LicenseVerificationEvent> Reader => _channel.Reader;

    public void Enqueue(LicenseVerificationEvent evt)
    {
        if (_channel.Writer.TryWrite(evt))
        {
            return;
        }

        var total = Interlocked.Increment(ref _dropped);
        _log.LogWarning(
            "Verification audit queue is full; dropped event for license {LicenseId}. Total dropped this run: {Dropped}.",
            evt.LicenseId, total);
    }
}
