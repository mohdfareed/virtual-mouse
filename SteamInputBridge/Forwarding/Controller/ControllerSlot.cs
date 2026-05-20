using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamInputBridge.Forwarding.Controller;

internal sealed class ControllerSlot(ControllerId controllerId, Action<ControllerSlot, ControllerFeedback> feedback)
{
    private IDisposable? _feedbackSubscription;
    private ControllerFeedback? _heldFeedback;
    private FeedbackTarget? _feedbackTarget;

    public ControllerId ControllerId { get; private set; } = controllerId;

    public ControllerEndpointState? Physical { get; set; }

    public Dictionary<ControllerEndpointId, ControllerEndpointState> ClientEndpoints { get; } = [];

    public IControllerOutput? Output { get; private set; }

    public ControllerOutput OutputKind { get; private set; }

    public bool HasEndpoints => Physical.HasValue || ClientEndpoints.Count != 0;

    // MARK: Endpoints
    // ========================================================================

    public bool HasClient(Guid? clientId)
    {
        return clientId.HasValue && FindClient(clientId.Value) is not null;
    }

    public void RemoveClient(Guid clientId)
    {
        foreach (ControllerEndpointId endpointId in ClientEndpoints.Keys.Where(id => id.ClientId == clientId).ToArray())
        {
            StopFeedbackTarget(new FeedbackTarget(endpointId));
            _ = ClientEndpoints.Remove(endpointId);
        }
    }

    public void RemovePhysical()
    {
        StopFeedbackTarget(FeedbackTarget.Physical);
        Physical = null;
    }

    public bool TryGetMergedState(
        Guid clientId,
        ControllerFeatures clientFeatures,
        ControllerFeatures physicalFallbackFeatures,
        out ControllerState state)
    {
        state = default;
        if (FindClient(clientId) is not { } client)
        {
            return false;
        }

        ControllerEndpointState? physical = Physical;
        state = new ControllerState(
            Select(client, physical, clientFeatures, physicalFallbackFeatures, ControllerFeatures.StandardControls)
                .Standard,
            Select(client, physical, clientFeatures, physicalFallbackFeatures, ControllerFeatures.Motion)
                .Motion,
            Select(client, physical, clientFeatures, physicalFallbackFeatures, ControllerFeatures.Touchpad)
                .Touchpad);
        return true;
    }

    public void ApplyFeedback(Guid clientId, ControllerFeedback feedback)
    {
        FeedbackTarget? previous = _feedbackTarget;
        FeedbackTarget? target = null;

        foreach (FeedbackTarget candidate in FindFeedbackTargets(clientId, feedback))
        {
            if (SendFeedback(candidate, feedback))
            {
                target = candidate;
                break;
            }
        }

        if (previous is { } previousTarget && previousTarget != target)
        {
            StopFeedbackTarget(previousTarget);
        }

        _heldFeedback = feedback;
        _feedbackTarget = target;
    }

    public ControllerEndpointState? FindClient(Guid clientId)
    {
        foreach (KeyValuePair<ControllerEndpointId, ControllerEndpointState> endpoint in ClientEndpoints)
        {
            if (endpoint.Key.ClientId == clientId)
            {
                return endpoint.Value;
            }
        }

        return null;
    }

    // MARK: Output
    // ========================================================================

    public void ConnectOutput(IControllerOutputFactory factory, ControllerOutput outputKind)
    {
        if (Output is not null && OutputKind == outputKind)
        {
            return;
        }

        DisconnectOutput();
        Output = factory.Connect(ControllerId, outputKind);
        OutputKind = outputKind;
        _feedbackSubscription = Output.ListenFeedback(update => feedback(this, update));
    }

    public void UpdateControllerId(ControllerId controllerId)
    {
        if (string.IsNullOrWhiteSpace(ControllerId.DisplayName) &&
            !string.IsNullOrWhiteSpace(controllerId.DisplayName))
        {
            ControllerId = controllerId;
        }
    }

    public void DisconnectOutput(List<IControllerOutput>? dispose = null)
    {
        StopHeldFeedback();
        IControllerOutput? output = Output;
        if (output is null)
        {
            return;
        }

        _feedbackSubscription?.Dispose();
        _feedbackSubscription = null;
        Output = null;
        OutputKind = ControllerOutput.None;

        if (dispose is not null)
        {
            dispose.Add(output);
        }
        else
        {
            output.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public void RetargetFeedback(Guid? clientId)
    {
        if (_heldFeedback is not { } feedback)
        {
            return;
        }

        if (!clientId.HasValue)
        {
            StopHeldFeedback();
            return;
        }

        ApplyFeedback(clientId.Value, feedback);
    }

    public void ReplayFeedback(Guid clientId)
    {
        if (_heldFeedback is { } feedback)
        {
            ApplyFeedback(clientId, feedback);
        }
    }

    public void StopHeldFeedback()
    {
        if (_feedbackTarget is { } target)
        {
            StopFeedbackTarget(target);
        }

        _heldFeedback = null;
        _feedbackTarget = null;
    }

    // MARK: Privates
    // ========================================================================

    private IEnumerable<FeedbackTarget> FindFeedbackTargets(Guid clientId, ControllerFeedback feedback)
    {
        if (feedback.IsEmpty)
        {
            yield break;
        }

        foreach (KeyValuePair<ControllerEndpointId, ControllerEndpointState> endpoint in ClientEndpoints)
        {
            if (endpoint.Key.ClientId == clientId && endpoint.Value.CanAccept(feedback))
            {
                yield return new FeedbackTarget(endpoint.Key);
            }
        }

        if (Physical is { } physical && physical.CanAccept(feedback))
        {
            yield return FeedbackTarget.Physical;
        }
    }

    private bool SendFeedback(FeedbackTarget target, ControllerFeedback feedback)
    {
        return target.EndpointId is { } endpointId
            ? ClientEndpoints.TryGetValue(endpointId, out ControllerEndpointState client) &&
            client.TrySendFeedback(feedback)
            : Physical?.TrySendFeedback(feedback) ?? false;
    }

    private void StopFeedbackTarget(FeedbackTarget target)
    {
        if (_heldFeedback is null)
        {
            return;
        }

        _ = SendFeedback(target, ControllerFeedback.StopRumble);
    }

    private static ControllerState Select(
        ControllerEndpointState client,
        ControllerEndpointState? physical,
        ControllerFeatures clientFeatures,
        ControllerFeatures physicalFallbackFeatures,
        ControllerFeatures feature)
    {
        return (clientFeatures & feature) != 0 && client.Supports(feature)
            ? client.State
            : physical is { } fallback &&
            (physicalFallbackFeatures & feature) != 0 &&
            fallback.Supports(feature)
            ? fallback.State
            : ControllerState.Empty;
    }
}

internal readonly record struct ControllerEndpointState(
    ControllerState State,
    ControllerFeatures Features,
    IControllerFeedbackSink? FeedbackSink)
{
    public bool Supports(ControllerFeatures feature)
    {
        return (Features & feature) == feature;
    }

    public bool CanAccept(ControllerFeedback feedback)
    {
        ControllerFeatures required = feedback.RequiredFeatures;
        return FeedbackSink is not null && required != ControllerFeatures.None && Supports(required);
    }

    public bool TrySendFeedback(ControllerFeedback feedback)
    {
        return CanAccept(feedback) && FeedbackSink!.TrySendFeedback(feedback);
    }
}

internal readonly record struct FeedbackTarget(ControllerEndpointId? EndpointId)
{
    public static FeedbackTarget Physical { get; } = new(null);
}
