using System;
using System.Collections.Generic;
using System.Text;

namespace ScreenConnect.Integration
{
    public enum SCHostSessionEventType
    {
        None = 0,
        Connected = 10,
        Disconnected = 11,
        CreatedSession = 20,
        EndedSession = 21,
        InitiatedJoin = 30,
        InvitedGuest = 31,
        AddedNote = 32,
        QueuedReinstall = 40,
        QueuedUninstall = 41,
        QueuedInvalidateLicense = 42,
        QueuedWake = 43,
        QueuedCommand = 44,
        QueuedMessage = 45,
        QueuedGuestInfoUpdate = 46,
        QueuedTool = 47,
        QueuedForceDisconnect = 48,
        ProcessedReinstall = 50,
        ProcessedUninstall = 51,
        ProcessedInvalidateLicense = 52,
        ProcessedWake = 53,
        ProcessedCommand = 54,
        ProcessedMessage = 55,
        ProcessedGuestInfoUpdate = 56,
        ProcessedTool = 57,
        ProcessedForceDisconnect = 58,
        ModifiedName = 60,
        ModifiedIsPublic = 61,
        ModifiedCode = 62,
        ModifiedHost = 63,
        ModifiedCustomProperty = 64,
        RanCommand = 70,
        SentMessage = 71,
        SentPrintJob = 80,
        ReceivedPrintJob = 81,
        CopiedText = 82,
        CopiedFiles = 83,
        DraggedFiles = 84,
        RanFiles = 85,
        SentFiles = 86
    }

    public class SCHostSessionEvent
    {
        #region Public Properties

        public Guid ConnectionId { get; internal set; }
        public String Data { get; internal set; }
        public Guid EventId { get; internal set; }
        public SCHostSessionEventType EventType { get; internal set; }
        public String Host { get; internal set; }
        public DateTime Time { get; internal set; }

        #endregion Public Properties
    }
}