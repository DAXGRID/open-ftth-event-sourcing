using Marten;
using System;

namespace OpenFTTH.EventSourcing.Postgres
{
    public class PostgresCommandLog : ICommandLog
    {
        private readonly IDocumentStore _store;

        public PostgresCommandLog(IDocumentStore store)
        {
            _store = store;
        }

        public CommandLogEntry Load(Guid id)
        {
            using (var session = _store.OpenSession())
            {
                return session.Load<CommandLogEntry>(id);
            }
        }

        public void Store(CommandLogEntry commandLogEntry)
        {
            using (var session = _store.LightweightSession())
            {
                session.Store(commandLogEntry);
                session.SaveChanges();
            }
        }
    }
}
