using FluentResults;
using OpenFTTH.CQRS;
using System;
using System.Collections.Generic;

namespace OpenFTTH.EventSourcing
{
    public record CommandLogEntry
    {
        public Guid Id { get; init; }
        public BaseCommand Command { get; init; }
        public bool IsSuccess { get; init; }
        public List<string> ErrorMessages { get; init; }

        public CommandLogEntry(BaseCommand command, Result result)
        {
            Id = command.CmdId;
            Command = command;

            if (result != null)
            {
                IsSuccess = result.IsSuccess;
                if (result.IsFailed)
                {
                    ErrorMessages = new List<string>();

                    foreach (var error in result.Errors)
                    {
                        ErrorMessages.Add(error.Message);
                    }
                }
            }
        }
    }
}
