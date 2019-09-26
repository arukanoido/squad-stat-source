using System;
using Tokens.Exceptions;

namespace SquadStatSourceWorker
{
    class MatchedEvent
    {
        public string Lines = null;
        public int CategoryIndex = 0;
        public Event Event = null;
        public string EventID = null;

        public void Clear()
        {
            Lines = null;
            CategoryIndex = 0;
            Event = null;
        }
    }

    class Parser
    {
        public static MatchedEvent MatchedEvent = new MatchedEvent();

        public static void Parse(string Line, Event[] List)
        {
            string EventID = Line.Substring(26, 3);
            if (MatchedEvent.Event != null)
            {
                if (MatchedEvent.CategoryIndex == MatchedEvent.Event.Category.Length
                    || (MatchedEvent.Event.WithinEventBoundary 
                        && !EventID.Equals(MatchedEvent.EventID, StringComparison.Ordinal))
                )
                {
                    //Squad.Server.Line = MatchedEvent.Lines;
                    MatchedEvent.Event.Parse(MatchedEvent.Lines);
                    MatchedEvent.Clear();
                }
                else if (HasMatch(Line, MatchedEvent.Event, MatchedEvent.CategoryIndex))
                {
                    MatchedEvent.Lines += Line + "\n";
                    MatchedEvent.CategoryIndex++;
                    return;
                }
            }

            foreach (var Event in List)
            {
                if (HasMatch(Line, Event, 0))
                {
                    if (MatchedEvent.Event != null)
                    {
                        //Squad.Server.Line = MatchedEvent.Lines;
                        MatchedEvent.Event.Parse(MatchedEvent.Lines);
                        MatchedEvent.Clear();
                    }
                    MatchedEvent.Event = Event;
                    MatchedEvent.Lines += Line + "\n";
                    MatchedEvent.CategoryIndex++;
                    MatchedEvent.EventID = EventID;
                    break;
                }
            }
        }

        public static bool HasMatch(string Line, Event Event, int Index)
        {
            if (Line.Length > 30 + Event.Category[Index].Length
                && Line.IndexOf(Event.Category[Index], 30, Event.Category[Index].Length, StringComparison.Ordinal) == 30)
            {
                if (Event.Contains != null && !Line.Contains(Event.Contains)) { return false; }

                return true;
            }
            return false;
        }
    }
}

namespace Tokens.Validators
{
    public class NotEqualValidator : ITokenValidator
    {
        public bool IsValid(object value, params string[] args)
        {
            if (args.Length == 0)
            {
                throw new ValidationException("You must specify the string to check equality to.");
            }

            var valueString = value.ToString();

            if (string.IsNullOrEmpty(valueString)) return false;

            return !valueString.Equals(args[0], StringComparison.OrdinalIgnoreCase);
        }
    }

    public class EqualsValidator : ITokenValidator
    {
        public bool IsValid(object value, params string[] args)
        {
            if (args.Length == 0)
            {
                throw new ValidationException("You must specify the string to check equality to.");
            }

            var valueString = value.ToString();

            if (string.IsNullOrEmpty(valueString)) return false;

            return valueString.Equals(args[0], StringComparison.OrdinalIgnoreCase);
        }
    }
}

namespace Tokens.Transformers
{
    public class ChopEndTransformer : ITokenTransformer
    {
        public bool CanTransform(object value, string[] args, out object transformed)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString()))
            {
                transformed = string.Empty;
                return true;
            }

            if (args == null || args.Length == 0) throw new TokenizerException($"ChopEnd(): missing argument processing: {value}");

            transformed = value.ToString().Remove(value.ToString().Length - 1, Convert.ToInt32(args[0]));

            return true;
        }
    }
}