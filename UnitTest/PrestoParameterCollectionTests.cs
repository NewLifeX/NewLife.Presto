using System;
using System.Data;
using NewLife.Presto;

namespace UnitTest;

/// <summary>参数集合测试</summary>
public class PrestoParameterCollectionTests
{
    [Fact(DisplayName = "添加与查找参数")]
    public void AddAndFind()
    {
        var collection = new PrestoParameterCollection();
        var parameter = new PrestoParameter("@a", 1);

        Assert.Equal(0, collection.Add(parameter));
        Assert.Equal(1, collection.Count);
        Assert.True(collection.Contains(parameter));
        Assert.True(collection.Contains("@A"));
        Assert.False(collection.Contains("@b"));
        Assert.False(collection.Contains("x"));
        Assert.Equal(0, collection.IndexOf(parameter));
        Assert.Equal(0, collection.IndexOf("@a"));
        Assert.Equal(-1, collection.IndexOf(new PrestoParameter()));
        Assert.Equal(-1, collection.IndexOf((Object)new Object()));

        var added = collection.AddWithValue("@b", 2);
        Assert.Equal("@b", added.ParameterName);
        Assert.Equal(2, added.Value);
        Assert.Equal(2, collection.Count);
        Assert.Same(added, collection["@B"]);
    }

    [Fact(DisplayName = "非法参数类型抛出异常")]
    public void AddInvalid()
    {
        var collection = new PrestoParameterCollection();

        Assert.Throws<ArgumentNullException>(() => collection.Add(null!));
        Assert.Throws<ArgumentException>(() => collection.Add("x"));
        Assert.Throws<ArgumentNullException>(() => collection.Insert(0, null!));
        Assert.Throws<ArgumentException>(() => collection.Insert(0, "x"));
        Assert.Throws<ArgumentNullException>(() => collection.AddRange(null!));
    }

    [Fact(DisplayName = "批量添加、复制与清空")]
    public void AddRangeCopyClear()
    {
        var collection = new PrestoParameterCollection();
        collection.AddRange(new Object[] { new PrestoParameter("@a", 1), new PrestoParameter("@b", 2) });

        var array = new Object[2];
        collection.CopyTo(array, 0);
        Assert.Equal(2, array.Length);

        var names = new System.Collections.Generic.List<String>();
        foreach (PrestoParameter item in collection) names.Add(item.ParameterName);
        Assert.Equal(new[] { "@a", "@b" }, names);

        collection.Clear();
        Assert.Equal(0, collection.Count);
    }

    [Fact(DisplayName = "插入、移除与索引器")]
    public void InsertRemoveIndexer()
    {
        var collection = new PrestoParameterCollection
        {
            new PrestoParameter("@a", 1),
            new PrestoParameter("@c", 3),
        };

        collection.Insert(1, new PrestoParameter("@b", 2));
        Assert.Equal(new[] { "@a", "@b", "@c" }, new[] { collection[0].ParameterName, collection[1].ParameterName, collection[2].ParameterName });

        collection.RemoveAt(1);
        Assert.Equal(2, collection.Count);
        collection.Remove(collection[0]);
        Assert.Single(collection);

        collection.Remove("x");
        collection.RemoveAt("missing");
        Assert.Single(collection);

        collection.RemoveAt("@c");
        Assert.Empty(collection);

        Assert.Throws<ArgumentException>(() => _ = collection["missing"]);
        Assert.Throws<ArgumentException>(() => collection["missing"] = new PrestoParameter());

        collection.Add(new PrestoParameter("@x", 1));
        collection["@x"] = new PrestoParameter("@y", 2);
        Assert.Equal("@y", collection[0].ParameterName);

        var db = (System.Data.Common.DbParameterCollection)collection;
        Assert.Throws<ArgumentException>(() => db[0] = new FakeParameter());
        Assert.Throws<ArgumentException>(() => db["missing"] = new FakeParameter());
    }

    private sealed class FakeParameter : System.Data.Common.DbParameter
    {
        public override String ParameterName { get; set; } = String.Empty;

        public override Object? Value { get; set; }

        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override Boolean IsNullable { get; set; }

        public override Int32 Size { get; set; }

        public override String SourceColumn { get; set; } = String.Empty;

        public override Boolean SourceColumnNullMapping { get; set; }

        public override DataRowVersion SourceVersion { get; set; }

        public override void ResetDbType() { }
    }

    [Fact(DisplayName = "集合属性")]
    public void CollectionProperties()
    {
        var collection = new PrestoParameterCollection();

        Assert.False(collection.IsFixedSize);
        Assert.False(collection.IsReadOnly);
        Assert.False(collection.IsSynchronized);
        Assert.NotNull(collection.SyncRoot);
    }

    [Fact(DisplayName = "参数默认值与重置")]
    public void ParameterDefaults()
    {
        var parameter = new PrestoParameter();
        Assert.Equal(DataRowVersion.Current, parameter.SourceVersion);
        Assert.Equal(ParameterDirection.Input, parameter.Direction);
        Assert.Equal(String.Empty, parameter.ParameterName);
        Assert.Equal(String.Empty, parameter.SourceColumn);

        parameter.ParameterName = null;
        parameter.SourceColumn = null;
        Assert.Equal(String.Empty, parameter.ParameterName);
        Assert.Equal(String.Empty, parameter.SourceColumn);

        parameter.DbType = DbType.Int32;
        parameter.ResetDbType();
        Assert.Equal(default(DbType), parameter.DbType);

        var typed = new PrestoParameter { ParameterName = "@x", DbType = DbType.Int64 };
        Assert.Equal(DbType.Int64, typed.DbType);
    }
}
