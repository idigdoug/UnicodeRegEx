namespace msandbox
{
    using System;
    using System.Runtime.InteropServices;
    using Debug = System.Diagnostics.Debug;

    internal ref struct RegExSegmentEnumerator
    {
        private RepStrRegEx.IRegExMatchEnumerator inner;   // readonly
        private RegExPinnedBytes input;                 // readonly
        private nuint begin;
        private nuint end;
        private nuint matchEnd;
        private State state;

        internal RegExSegmentEnumerator(RepStrRegEx.IRegExMatchEnumerator inner, RegExPinnedBytes input)
        {
            this.inner = inner;
            this.input = input;
            this.begin = 0;
            this.end = 0;
            this.matchEnd = 0;
            this.state = State.BeforeBegin;
        }

        public RegExSegment Current =>
            state > State.BeforeBegin
            ? new RegExSegment(inner, input, begin, end, state == State.AtMatch)
            : throw new InvalidOperationException("Enumeration is before-begin or after-end.");

        public RegExSegmentEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            Debug.Assert(begin <= end);
            Debug.Assert(end <= input.Size);

            switch (state)
            {
                default: // State.End, or invalid state.

                    throw new InvalidOperationException("Enumeration is after-end.");

                case State.BeforeBegin:
                case State.AtMatch:

                    if (inner.NextMatch())
                    {
                        var match = inner.GetSubMatch(0);
                        var offset = checked((nuint)match.offset);
                        var size = checked((nuint)match.size);
                        if (offset == end)
                        {
                            begin = offset;
                            end = checked(offset + size);
                            state = State.AtMatch;
                        }
                        else
                        {
                            begin = end;
                            end = offset;
                            matchEnd = checked(offset + size);
                            state = State.BeforeMatch;
                        }
                    }
                    else if (input.Size != end)
                    {
                        begin = end;
                        end = input.Size;
                        state = State.Tail;
                    }
                    else
                    {
                        begin = end;
                        state = State.End;
                    }

                    break;

                case State.BeforeMatch:

                    begin = end;
                    end = matchEnd;
                    state = State.AtMatch;
                    break;

                case State.Tail:

                    Debug.Assert(input.Size == end);
                    begin = end;
                    state = State.End;
                    break;
            }

            Debug.Assert(begin <= end);
            Debug.Assert(end <= input.Size);

            return state > State.BeforeBegin;
        }

        public void Dispose()
        {
            if (inner != null)
            {
                Marshal.FinalReleaseComObject(inner);
            }
        }

        private enum State
        {
            End = -1,
            BeforeBegin = 0,
            BeforeMatch,
            AtMatch,
            Tail,
        }
    }
}
