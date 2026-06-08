namespace Frame.Timing
{
    public struct TimerHandle
    {
        private readonly TimerService service;
        private readonly int id;

        internal TimerHandle(TimerService service, int id)
        {
            this.service = service;
            this.id = id;
        }

        public int Id
        {
            get { return id; }
        }

        public bool IsValid
        {
            get { return service != null && id > 0 && service.Contains(id); }
        }

        public void Cancel()
        {
            if (service != null)
            {
                service.Cancel(id);
            }
        }
    }
}
