namespace Rutin.GunShop
{
    public readonly struct DualHandCommandFrame
    {
        public static DualHandCommandFrame Neutral =>
            new(HandCommand.Neutral, HandCommand.Neutral);

        public DualHandCommandFrame(HandCommand left, HandCommand right)
        {
            Left = left;
            Right = right;
        }

        public HandCommand Left { get; }

        public HandCommand Right { get; }

        public HandCommand GetCommand(HandSide side)
        {
            return side == HandSide.Left ? Left : Right;
        }
    }
}
