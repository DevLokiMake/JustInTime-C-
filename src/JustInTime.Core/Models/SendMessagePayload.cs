namespace JustInTime.Core.Models;

public sealed record SendMessagePayload(MessengerApp App, string Recipient, string Text) : TaskPayload
{
    public override ActionType GetActionType() => ActionType.SendMessage;

    public override TaskPayload Normalize() => this with
    {
        Recipient = (Recipient ?? string.Empty).Trim(),
        Text = (Text ?? string.Empty).Trim()
    };
}
