namespace Voron.Tests.Trees.WorkingWithStructs
{
    internal class TestClass
    {
        // HAVE to use Properties and they HAVE to be public, virtual, writable and readable!!
        // Otherwise we can't override them (standard pattern, see Entity Framework and other ORM's)
        public virtual int Attempts { get; set; }
        public virtual int Errors { get; set; }
        public virtual int Successes { get; set; }
        public virtual byte IsValid { get; set; }
        public virtual long IndexedAt { get; set; }
        public virtual string Text { get; set; }
    }
}
