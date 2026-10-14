namespace Exportaramus.Features.Snapshots;

public enum CaptureRequest { None, AutomaticLightweight, AutomaticFull, ManualFull }

public sealed class SnapshotSchedulePolicy
{
    public const double LoginDelay = 15;
    public const double PresenceInterval = 300;
    public const double FullInterval = 1800;
    private double presenceDue = double.PositiveInfinity;
    private double fullDue = double.PositiveInfinity;
    private double retryDue = double.PositiveInfinity;
    private CaptureRequest retryRequest;
    private int failures;
    public int FailureCount => failures;
    public CaptureRequest Pending { get; private set; }

    public void Login(double now) { Reset(); fullDue = now + LoginDelay; presenceDue = fullDue; }
    public void Reset() { presenceDue = fullDue = retryDue = double.PositiveInfinity; failures = 0; Pending = CaptureRequest.None; retryRequest = CaptureRequest.None; }
    public void Request(CaptureRequest request)
    {
        if (request == CaptureRequest.ManualFull) { failures = 0; retryDue = double.PositiveInfinity; }
        if (request > Pending) Pending = request;
    }
    public void Tick(double now)
    {
        if (now >= fullDue) Request(CaptureRequest.AutomaticFull);
        else if (now >= presenceDue) Request(CaptureRequest.AutomaticLightweight);
        if (now >= retryDue) Request(retryRequest);
    }
    public CaptureRequest Take()
    {
        var request = Pending; Pending = CaptureRequest.None;
        return request;
    }
    public void Accepted(bool full, double now)
    {
        presenceDue = now + PresenceInterval;
        if (full) fullDue = now + FullInterval;
        failures = 0; retryDue = double.PositiveInfinity; retryRequest = CaptureRequest.None;
    }
    public void Failed(CaptureRequest request, double now, bool cancelled)
    {
        if (request is CaptureRequest.None or CaptureRequest.ManualFull || cancelled) return;
        var delay = failures++ switch { 0 => 30, 1 => 120, _ => 300 };
        retryRequest = request;
        retryDue = now + delay;
        // Move an already elapsed due time to its next normal cadence so it cannot bypass backoff.
        if (request == CaptureRequest.AutomaticFull && fullDue <= now) fullDue = now + FullInterval;
        if (presenceDue <= now) presenceDue = now + PresenceInterval;
    }
}
