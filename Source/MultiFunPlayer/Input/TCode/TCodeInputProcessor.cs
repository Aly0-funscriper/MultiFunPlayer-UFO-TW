using MultiFunPlayer.Common;
using NLog;
using Stylet;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Input.TCode;

internal sealed partial class TCodeInputProcessor(IEventAggregator eventAggregator) : AbstractInputProcessor(eventAggregator)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly SplittingStringBuffer _buffer = new('\n');
    private readonly Dictionary<string, int> _unsignedAxisStates = [];
    private readonly Dictionary<string, int> _signedAxisStates = [];

    public void Parse(string input)
    {
        _buffer.Push(input);
        foreach (var command in _buffer.Consume())
            ParseCommand(command);
    }

    private void ParseCommand(string command)
    {
        Logger.Trace("Parsing {0}", command);
        foreach (var match in ButtonRegex.Matches(command).Where(m => m.Success))
            CreateButtonGesture(match);

        foreach (var match in UnsignedAxisRegex.Matches(command).Where(m => m.Success))
            CreateAxisGesture(_unsignedAxisStates, match, ushort.MinValue, ushort.MaxValue);

        foreach (var match in SignedAxisRegex.Matches(command).Where(m => m.Success))
            CreateAxisGesture(_signedAxisStates, match, short.MinValue, short.MaxValue);

        void CreateButtonGesture(Match match)
        {
            var button = match.Groups["button"].Value;
            var state = int.Parse(match.Groups["state"].Value) == 1;
            PublishGesture(TCodeButtonGesture.Create(button, state));
        }

        void CreateAxisGesture(Dictionary<string, int> states, Match match, double minValue, double maxValue)
        {
            var axis = match.Groups["axis"].Value;
            var value = int.Parse(match.Groups["value"].Value);
            states.TryAdd(axis, 0);

            var delta = value - states[axis];
            if (delta == 0)
                return;

            var valueDecimal = MathUtils.UnLerp(minValue, maxValue, value);
            var deltaDecimal = MathUtils.UnLerp(minValue, maxValue, delta);

            PublishGesture(TCodeAxisGesture.Create(axis, valueDecimal, deltaDecimal, 0));
            states[axis] = value;
        }
    }

    [GeneratedRegex("#(?<button>.+?):(?<state>0|1)")]
    private static partial Regex ButtonRegex { get; }
    [GeneratedRegex(@"@(?<axis>.+?):(?<value>\d{1,5})")]
    private static partial Regex UnsignedAxisRegex { get; }
    [GeneratedRegex(@"\$(?<axis>.+?):(?<value>-?\d{1,5})")]
    private static partial Regex SignedAxisRegex { get; }
}
