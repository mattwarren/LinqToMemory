using System;

namespace Voron.Tests.Trees.WorkingWithStructs
{
    internal class StructureSchemaWithoutEnums<TField> : StructureSchema<TField>
    {
        public StructureSchemaWithoutEnums()
            : base(onlyAllowEnums: false)
        {
            // We want to allow non-enum fields, so we can be more dynamic!!
        }

        public new StructureField[] Fields { get { return base.Fields; } }

        public StructureSchemaWithoutEnums<TField> Add<T>(int index, object field)
        {
            return (StructureSchemaWithoutEnums<TField>)base.Add(typeof(T), index, field);
        }

        public new StructureSchemaWithoutEnums<TField> Add(Type type, int index, object field)
        {
            return (StructureSchemaWithoutEnums<TField>)base.Add(type, index, field);
        }
    }
}
