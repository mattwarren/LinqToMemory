using System;
using System.Reflection;

namespace Voron.Tests.Trees.WorkingWithStructs
{
    internal static class ProxyGenerator
    {
        internal static StructureSchemaWithoutEnums<int> GetSchema<T>() where T : class
        {
            var classType = typeof(T);
            if (classType == typeof(TestClass))
            {
                // TODO string and byte[] properties have to come last as they're variable sized fields,
                // otherwise the call to schema.Add(..) will throw an error!!

                var schema = new StructureSchemaWithoutEnums<int>();
                var counter = 0;
                // We only proxy public, virtual writable and readable propeties that aren't static!!
                // And Voron only accepts certain types, so check for those as well
                foreach (var property in classType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var propertyType = property.PropertyType;
                    var validType = propertyType == typeof(string) || propertyType == typeof(byte[]) ||
                                    propertyType.IsPrimitive || propertyType == typeof(decimal);
                    var isVirtual = property.GetSetMethod().IsVirtual &&
                                    property.GetSetMethod().IsFinal == false;
                    var readWrite = property.CanWrite && property.CanRead;
                    //Console.WriteLine("{0,10}: Type{1}, validType={2}, isVirtual={3}, readWrite={4}",
                    //                  property.Name, propertyType, validType, isVirtual, readWrite);
                    if (validType == false || readWrite == false || isVirtual == false)
                    {
                        // Maybe throw an error here, otherwise we're silently ignoring a property and the user doesn't know why
                        continue;
                    }

                    schema.Add(propertyType, counter, property.Name);
                    counter++;
                }
                return schema;
            }
            else
            {
                throw new InvalidOperationException("Can't currently create a schema for " + typeof(T).FullName);
            }
        }

        public static T CreateWriter<T>(Structure<int> writer) where T : class
        {
            if (typeof(T) == typeof(TestClass))
                return new TestClassProxy(writer) as T;
            else
                throw new InvalidOperationException("Can't currently create a proxy for " + typeof(T).FullName);
        }

        public static T CreateReader<T>(StructureReader<int> reader) where T : class
        {
            if (typeof(T) == typeof(TestClass))
                return new TestClassProxy(reader) as T;
            else
                throw new InvalidOperationException("Can't currently create a proxy for " + typeof(T).FullName);
        }

        // This proxy class would be dynamicially generated at run-time, using IL.Emit (or Sigil)
        internal class TestClassProxy : TestClass
        {
            private readonly Structure<int> writer = null;
            private readonly StructureReader<int> reader = null;

            public TestClassProxy(Structure<int> writer)
            {
                this.writer = writer;
            }

            public TestClassProxy(StructureReader<int> reader)
            {
                this.reader = reader;
            }

            private const int AttemptsOffset = 0;
            public override int Attempts
            {
                get { return reader.ReadInt(AttemptsOffset); }
                set { writer.Set(AttemptsOffset, value); }
            }

            private const int ErrorsOffset = 1;
            public override int Errors
            {
                get { return reader.ReadInt(ErrorsOffset); }
                set { writer.Set(ErrorsOffset, value); }
            }

            private const int SuccessesOffset = 2;
            public override int Successes
            {
                get { return reader.ReadInt(SuccessesOffset); }
                set { writer.Set(SuccessesOffset, value); }
            }

            private const int IsValidOffset = 3;
            public override byte IsValid
            {
                get { return reader.ReadByte(IsValidOffset); }
                set { writer.Set(IsValidOffset, (byte)value); }
            }

            private const int IndexedAtOffset = 4;
            public override long IndexedAt
            {
                get { return reader.ReadLong(IndexedAtOffset); }
                set { writer.Set(IndexedAtOffset, value); }
            }

            private const int TextOffset = 5;
            public override string Text
            {
                get { return reader.ReadString(TextOffset); }
                set { writer.Set(TextOffset, value); }
            }
        }
    }
}
