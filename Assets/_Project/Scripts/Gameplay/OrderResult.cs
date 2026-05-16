namespace APlaceLikeMe.Gameplay
{
    public readonly struct OrderResult
    {
        public OrderResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        public bool Succeeded { get; }
        public string Message { get; }
    }
}
