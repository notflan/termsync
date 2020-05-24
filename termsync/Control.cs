using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace termsync
{
    enum DeleteLocation
    {
        Before, //Backspace
        After, //DEL
    }
    enum MoveDirection
    {
        Left,Right,
    }
    enum ControlType
    {
        Print, //WriteLine

        Move, //Move the cursor
        Delete, //Delete a character from input
        Commit, //Commit input
        Echo, // Add a character to input

        ChangePrompt,
    }
    readonly struct ControlValue
    {
        public readonly ControlType Type;

        public readonly object Value;

        public readonly TaskCompletionSource<object> Processed;

        public ControlValue(ControlType type, object value)
        {
            Type = type;
            Value = value;

            Processed = new TaskCompletionSource<object>();
        }
    }
}
