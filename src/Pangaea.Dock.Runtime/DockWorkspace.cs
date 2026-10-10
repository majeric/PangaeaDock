using System.Collections.Immutable;
using Pangaea.Dock.Core;

namespace Pangaea.Dock.Runtime;

public sealed record WorkspaceChange(string Description, bool Structural);
internal sealed record HistoryEntry(DockLayout Before, DockLayout After, string Description);

/// <summary>
/// Owns panel sessions and layout history. Call on a single application/UI thread. Guards may be asynchronous;
/// structural commands are rejected while a transaction is pending. Dispose when the workspace closes.
/// </summary>
public sealed class DockWorkspace : IDisposable
{
    private readonly Dictionary<string, PanelSession> _sessions = [];
    private readonly Dictionary<string, PanelState> _sessionStates = [];
    private readonly List<HistoryEntry> _undo = [], _redo = [];
    private bool _busy, _disposed;
    public DockLayout Layout { get; private set; }
    public PanelRegistry Registry { get; }
    public int HistoryLimit { get; set; } = 100;
    public bool IsLayoutLocked { get; set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.LastOrDefault()?.Description;
    public string? RedoDescription => _redo.LastOrDefault()?.Description;
    public event Action<WorkspaceChange>? Changed;
    public event Action<string>? Diagnostic;
    /// <summary>Called before a session loses its last live slot, once per panel. Cancellation leaves the layout
    /// unchanged, but cannot undo side effects an earlier panel's approval already performed.</summary>
    public Func<PanelState, PanelSession, CancellationToken, Task<bool>>? CanReleaseAsync { get; set; }
    /// <summary>Optional presenter preparation. Throw to abort before publication. Do not attach visuals here.</summary>
    public Action<DockLayout>? PreparePresentation { get; set; }
    /// <summary>Consumer policy; return a reason to reject an operation before evaluation.</summary>
    public Func<DockOperation, string?>? OperationPolicy { get; set; }
    public DockWorkspace(PanelRegistry registry, DockLayout? initial = null)
    {
        Registry = registry; Layout = initial ?? DockLayout.Empty(); LayoutTree.Validate(Layout); ValidateMultiplicity(Layout);
        var created = new List<string>();
        try { Prepare(Layout, created); }
        catch { foreach (var id in created) _sessions[id].Dispose(); throw; }
    }
    public PanelSession Session(string instanceId) => _sessions[instanceId];
    public bool IsRetained(string instanceId) => _sessions.ContainsKey(instanceId);
    public OperationResult Evaluate(DockOperation operation)
    {
        if (_disposed) return new(null, "The workspace is closed.");
        if (OperationPolicy?.Invoke(operation) is { } reason) return new(null, reason);
        if (IsLayoutLocked && operation is not SelectTab) return new(null, "Unlock the layout to rearrange panels.");
        // A singleton retained by undo is still the same logical instance when reopened.
        if (operation is AddPanel add && Registry.Descriptors.TryGetValue(add.Panel.TypeId, out var descriptor) && descriptor.Singleton)
        {
            if (add.ReplaceSlotId is not null && Layout.Slots.FirstOrDefault(s => s.Id == add.ReplaceSlotId) is { } replaced
                && Layout.Panels[replaced.InstanceId].TypeId == add.Panel.TypeId)
                return DockEngine.Evaluate(Layout, new SelectTab(add.ReplaceSlotId), Registry.Descriptors);
            var retained = _sessionStates.Values.FirstOrDefault(p => p.TypeId == add.Panel.TypeId && !Layout.Panels.ContainsKey(p.Id));
            if (retained is not null) operation = add with { Panel = retained };
        }
        return DockEngine.Evaluate(Layout, operation, Registry.Descriptors);
    }
    public async Task<bool> ExecuteAsync(DockOperation operation, CancellationToken cancellationToken = default)
    {
        var result = Evaluate(operation);
        if (result.Plan is null) { Report(result.Error!); return false; }
        return await CommitAsync(result.Plan, cancellationToken);
    }
    public Task<bool> CommitAsync(OperationPlan plan, CancellationToken cancellationToken = default) =>
        CommitInternalAsync(plan, null, null, cancellationToken);
    private async Task<bool> CommitInternalAsync(OperationPlan plan, bool? historyUndo, HistoryEntry? entry, CancellationToken token)
    {
        if (_disposed || _busy) { Report("A workspace operation is already pending or the workspace is closed."); return false; }
        if (plan.WorkspaceId != Layout.Id || plan.ExpectedRevision != Layout.Revision)
        { Report("The layout changed. Please try the operation again."); return false; }
        if (IsLayoutLocked && plan.RecordHistory) { Report("Unlock the layout first."); return false; }
        _busy = true; var created = new List<string>();
        try
        {
            token.ThrowIfCancellationRequested(); LayoutTree.Validate(plan.Candidate); ValidateMultiplicity(plan.Candidate);
            foreach (var panel in Layout.Panels.Values.Where(p => !plan.Candidate.Panels.ContainsKey(p.Id)))
                if (CanReleaseAsync is not null && !await CanReleaseAsync(panel, _sessions[panel.Id], token)) return false;
            token.ThrowIfCancellationRequested();
            if (_disposed) return false;
            if (plan.ExpectedRevision != Layout.Revision) { Report("The layout changed while waiting."); return false; }
            var from = historyUndo == true ? _undo : _redo; var to = historyUndo == true ? _redo : _undo;
            // History can be cleared while a release guard is pending; never publish a replay whose entry is gone.
            if (historyUndo.HasValue && (from.Count == 0 || !ReferenceEquals(from[^1], entry)))
            { Report("Layout history changed while waiting."); return false; }
            Prepare(plan.Candidate, created);
            PreparePresentation?.Invoke(plan.Candidate);
            token.ThrowIfCancellationRequested();
            var previous = Layout;
            Layout = plan.Candidate with { Id = previous.Id, Revision = previous.Revision + 1 };
            if (historyUndo.HasValue) { from.RemoveAt(from.Count - 1); to.Add(entry!); }
            else if (plan.RecordHistory)
            {
                _undo.Add(new(previous, Layout, plan.Description)); _redo.Clear();
                while (_undo.Count > Math.Max(1, HistoryLimit)) _undo.RemoveAt(0);
            }
            Collect(); Notify(new(plan.Description, plan.RecordHistory)); return true;
        }
        catch (OperationCanceledException) { Report("Layout operation canceled."); return false; }
        catch (Exception ex) { Report("Could not change the layout: " + ex.Message); return false; }
        finally
        {
            foreach (var id in created.Where(id => !Layout.Panels.ContainsKey(id)).ToArray())
                if (_sessions.Remove(id, out var session)) { _sessionStates.Remove(id); SafeDispose(session); }
            _busy = false;
        }
    }
    private void Prepare(DockLayout layout, List<string> created)
    {
        foreach (var state in layout.Panels.Values)
        {
            if (!_sessions.ContainsKey(state.Id)) { _sessions.Add(state.Id, Registry.Create(state)); _sessionStates.Add(state.Id, state); created.Add(state.Id); }
            else if (_sessionStates[state.Id].TypeId != state.TypeId)
                throw new InvalidDataException("Panel identity " + state.Id + " already belongs to another panel type.");
        }
    }
    private void ValidateMultiplicity(DockLayout layout)
    {
        foreach (var d in Registry.Descriptors.Values.Where(d => d.Singleton))
            if (layout.Panels.Values.Count(p => p.TypeId == d.Id) > 1) throw new InvalidDataException(d.Title + " can only be open once.");
    }
    public Task<bool> UndoAsync() => ReplayAsync(true);
    public Task<bool> RedoAsync() => ReplayAsync(false);
    /// <summary>Forget arrangement history after externally owned panels are opened/closed.</summary>
    public void ClearHistory() { _undo.Clear(); _redo.Clear(); Collect(); }
    private Task<bool> ReplayAsync(bool undo)
    {
        var stack = undo ? _undo : _redo;
        if (stack.Count == 0) return Task.FromResult(false);
        var entry = stack[^1];
        return CommitInternalAsync(new(Layout.Id, Layout.Revision, undo ? entry.Before : entry.After,
            (undo ? "Undo " : "Redo ") + entry.Description), undo, entry, default);
    }
    public Task<bool> AddAsync(string typeId, string groupId, string? replaceSlot = null)
    {
        var state = new PanelState(Ids.New(), typeId);
        return ExecuteAsync(new AddPanel(state, groupId, replaceSlot));
    }
    public Task<bool> DuplicateAsync(string slotId)
    {
        var slot = Layout.Slot(slotId); var state = Layout.Panels[slot.InstanceId];
        if (!Registry.Descriptors.TryGetValue(state.TypeId, out var d) || !d.CanDuplicate || d.Singleton)
        { Report("This panel does not support duplication."); return Task.FromResult(false); }
        var copy = state with { Id = Ids.New(), State = _sessions[state.Id].CaptureState() };
        return ExecuteAsync(new AddPanel(copy, Layout.Groups.First(g => g.Slots.Contains(slot)).Id));
    }
    public DockLayout Capture() => Layout with { Panels = Layout.Panels.ToImmutableDictionary(p => p.Key,
        p => p.Value with { State = _sessions[p.Key].CaptureState() }) };
    /// <summary>Apply an arrangement while retaining all current panels and their live state.</summary>
    public Task<bool> ApplyArrangementAsync(DockLayout template, string name)
    {
        try { LayoutTree.Validate(template); }
        catch (InvalidDataException e) { Report("Could not apply " + name + ": " + e.Message); return Task.FromResult(false); }
        var available = Layout.Panels.Values.ToList(); var mapping = new Dictionary<string, PanelState>();
        foreach (var state in template.Panels.Values)
        {
            var match = available.FirstOrDefault(p => p.Id == state.Id && p.TypeId == state.TypeId) ?? available.FirstOrDefault(p => p.TypeId == state.TypeId);
            if (match is not null) { mapping[state.Id] = match; available.Remove(match); }
            else
            {
                var retained = Registry.Descriptors.TryGetValue(state.TypeId, out var d) && d.Singleton
                    ? _sessionStates.Values.FirstOrDefault(p => p.TypeId == state.TypeId && !Layout.Panels.ContainsKey(p.Id)) : null;
                mapping[state.Id] = retained ?? state with { Id = Ids.New() };
            }
        }
        var slotMapping = template.Slots.ToDictionary(s => s.Id, s => Layout.Slots.FirstOrDefault(l => l.InstanceId == mapping[s.InstanceId].Id)?.Id ?? Ids.New());
        var next = LayoutTree.Map(template, n => n is TabGroup g ? g with
        {
            Slots = g.Slots.Select(s => new DockSlot(slotMapping[s.Id], mapping[s.InstanceId].Id)).ToImmutableArray(),
            ActiveSlotId = g.ActiveSlotId is null ? null : slotMapping[g.ActiveSlotId]
        } : n);
        next = next with { Panels = mapping.Values.Concat(available).ToImmutableDictionary(p => p.Id), Id = Layout.Id };
        var target = LayoutTree.Groups(next.Hosts.First(h => h.Id == next.MainHostId).Root).FirstOrDefault(g => g.RegionKey == "primary")
            ?? LayoutTree.Groups(next.Hosts.First(h => h.Id == next.MainHostId).Root).First();
        var leftovers = available.Select(p => Layout.Slots.First(s => s.InstanceId == p.Id)).ToImmutableArray();
        next = LayoutTree.Map(next, n => n.Id == target.Id && n is TabGroup g ? g with { Slots = g.Slots.AddRange(leftovers) } : n);
        return ExecuteAsync(new ApplyLayout(next, "Switch to " + name));
    }
    public void UpdatePlacement(string hostId, WindowPlacement placement)
    {
        if (_busy || _disposed || !Layout.Hosts.Any(h => h.Id == hostId)) return;
        // A native window can be smaller than a stored placement allows; store the smallest valid size.
        placement = placement with { Width = Math.Max(100, placement.Width), Height = Math.Max(100, placement.Height) };
        var candidate = Layout with { Hosts = Layout.Hosts.Select(h => h.Id == hostId ? h with { Placement = placement } : h).ToImmutableArray() };
        try { LayoutTree.Validate(candidate); }
        catch (InvalidDataException e) { Report("Window placement ignored: " + e.Message); return; }
        Layout = candidate with { Revision = Layout.Revision + 1 };
        // Native geometry notifications do not rebuild/reparent the visual tree.
        Notify(new("Window placement", false));
    }
    private void Collect()
    {
        var keep = Layout.Panels.Keys.Concat(_undo.Concat(_redo).SelectMany(e => e.Before.Panels.Keys.Concat(e.After.Panels.Keys))).ToHashSet();
        foreach (var id in _sessions.Keys.Where(id => !keep.Contains(id)).ToArray())
        { var session = _sessions[id]; _sessions.Remove(id); _sessionStates.Remove(id); SafeDispose(session); }
    }
    private void SafeDispose(PanelSession session) { try { session.Dispose(); } catch (Exception e) { Report("Panel disposal: " + e.Message); } }
    private void Notify(WorkspaceChange change)
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
            try { ((Action<WorkspaceChange>)handler)(change); } catch (Exception e) { Report("Presenter notification: " + e.Message); }
    }
    public void Report(string message)
    {
        foreach (var handler in Diagnostic?.GetInvocationList() ?? [])
            try { ((Action<string>)handler)(message); } catch { /* Diagnostics cannot break a transaction. */ }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; foreach (var session in _sessions.Values) SafeDispose(session);
        _sessions.Clear(); _sessionStates.Clear(); _undo.Clear(); _redo.Clear(); Changed = null; PreparePresentation = null;
    }
}
