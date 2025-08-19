namespace Rent.Motorcycle.Domain.Abstractions
{
    public abstract class Entity
    {
        public string Id { get; protected set; } = default!;
        public bool Active { get; protected set; } = true;
        public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UpdatedAt { get; protected set; }
        public DateTimeOffset? DeletedAt { get; protected set; }

        public virtual void Deactivate()
        {
            if (!Active) return;
            Active = false;
            DeletedAt = DateTimeOffset.UtcNow;
            UpdatedAt = DeletedAt;
        }
        protected void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
    }
}
