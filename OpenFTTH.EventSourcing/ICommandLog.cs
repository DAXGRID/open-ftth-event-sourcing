using System;

namespace OpenFTTH.EventSourcing
{
    public interface ICommandLog
    {
        void Store(CommandLogEntry commandLogEntry);
        CommandLogEntry Load(Guid id);
    }
}
