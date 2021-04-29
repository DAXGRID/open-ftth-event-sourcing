using System;
using System.Collections.Concurrent;

namespace OpenFTTH.EventSourcing.InMem
{
    public class InMemCommandLog : ICommandLog
    {
        private readonly ConcurrentDictionary<Guid, CommandLogEntry> _commandLogEntries = new();

        public CommandLogEntry Load(Guid id)
        {
            return _commandLogEntries[id];
        }

        public void Store(CommandLogEntry commandLogEntry)
        {
            if (commandLogEntry.Id == Guid.Empty)
                throw new ApplicationException("A command must have an non-empty guid id");

            _commandLogEntries[commandLogEntry.Id] = commandLogEntry;
        }
    }
}
