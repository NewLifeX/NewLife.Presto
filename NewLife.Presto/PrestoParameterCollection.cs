using System.Collections;
using System.Data.Common;

namespace NewLife.Presto;

/// <summary>参数集合</summary>
public class PrestoParameterCollection : DbParameterCollection
{
    #region 属性
    private readonly List<PrestoParameter> _items = [];

    /// <summary>同步根</summary>
    public override Object SyncRoot { get; } = new Object();

    /// <summary>参数数量</summary>
    public override Int32 Count => _items.Count;

    /// <summary>是否固定大小</summary>
    public override Boolean IsFixedSize => false;

    /// <summary>是否只读</summary>
    public override Boolean IsReadOnly => false;

    /// <summary>是否同步</summary>
    public override Boolean IsSynchronized => false;
    #endregion

    #region 方法
    /// <summary>添加参数</summary>
    /// <param name="value">参数对象</param>
    /// <returns>参数索引</returns>
    public override Int32 Add(Object value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));

        var parameter = value as PrestoParameter ?? throw new ArgumentException(Localization.Translate("Parameter must be of type PrestoParameter."), nameof(value));
        _items.Add(parameter);
        return _items.Count - 1;
    }

    /// <summary>添加参数并赋值</summary>
    /// <param name="parameterName">参数名</param>
    /// <param name="value">参数值</param>
    /// <returns>参数对象</returns>
    public PrestoParameter AddWithValue(String parameterName, Object? value)
    {
        var parameter = new PrestoParameter { ParameterName = parameterName, Value = value };
        _items.Add(parameter);
        return parameter;
    }

    /// <summary>批量添加参数</summary>
    /// <param name="values">参数数组</param>
    public override void AddRange(Array values)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));

        foreach (var item in values) Add(item);
    }

    /// <summary>清空参数</summary>
    public override void Clear() => _items.Clear();

    /// <summary>是否包含参数</summary>
    /// <param name="value">参数对象</param>
    /// <returns>是否包含</returns>
    public override Boolean Contains(Object value) => value is PrestoParameter parameter && _items.Contains(parameter);

    /// <summary>是否包含指定名称的参数</summary>
    /// <param name="parameterName">参数名</param>
    /// <returns>是否包含</returns>
    public override Boolean Contains(String parameterName) => IndexOf(parameterName) >= 0;

    /// <summary>复制到数组</summary>
    /// <param name="array">目标数组</param>
    /// <param name="index">起始索引</param>
    public override void CopyTo(Array array, Int32 index) => ((ICollection)_items).CopyTo(array, index);

    /// <summary>获取枚举器</summary>
    /// <returns>枚举器</returns>
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();

    /// <summary>获取参数索引</summary>
    /// <param name="parameterName">参数名</param>
    /// <returns>索引；未找到返回 -1</returns>
    public override Int32 IndexOf(String parameterName)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].ParameterName.EqualIgnoreCase(parameterName)) return i;
        }

        return -1;
    }

    /// <summary>获取参数索引</summary>
    /// <param name="value">参数对象</param>
    /// <returns>索引；未找到返回 -1</returns>
    public override Int32 IndexOf(Object value) => value is PrestoParameter parameter ? _items.IndexOf(parameter) : -1;

    /// <summary>插入参数</summary>
    /// <param name="index">索引</param>
    /// <param name="value">参数对象</param>
    public override void Insert(Int32 index, Object value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));

        var parameter = value as PrestoParameter ?? throw new ArgumentException(Localization.Translate("Parameter must be of type PrestoParameter."), nameof(value));
        _items.Insert(index, parameter);
    }

    /// <summary>移除参数</summary>
    /// <param name="value">参数对象</param>
    public override void Remove(Object value)
    {
        if (value is PrestoParameter parameter) _items.Remove(parameter);
    }

    /// <summary>移除指定索引处的参数</summary>
    /// <param name="index">索引</param>
    public override void RemoveAt(Int32 index) => _items.RemoveAt(index);

    /// <summary>移除指定名称的参数</summary>
    /// <param name="parameterName">参数名</param>
    public override void RemoveAt(String parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0) _items.RemoveAt(index);
    }
    #endregion

    #region 索引
    /// <summary>获取指定索引处的参数</summary>
    /// <param name="index">索引</param>
    /// <returns>参数对象</returns>
    protected override DbParameter GetParameter(Int32 index) => _items[index];

    /// <summary>获取指定名称的参数</summary>
    /// <param name="parameterName">参数名</param>
    /// <returns>参数对象</returns>
    protected override DbParameter GetParameter(String parameterName)
    {
        var index = IndexOf(parameterName);
        if (index < 0) throw new ArgumentException(Localization.Translate("Parameter '{0}' was not found.", parameterName), nameof(parameterName));

        return _items[index];
    }

    /// <summary>设置指定索引处的参数</summary>
    /// <param name="index">索引</param>
    /// <param name="value">参数对象</param>
    protected override void SetParameter(Int32 index, DbParameter value)
    {
        if (value is not PrestoParameter parameter) throw new ArgumentException(Localization.Translate("Parameter must be of type PrestoParameter."), nameof(value));

        _items[index] = parameter;
    }

    /// <summary>设置指定名称的参数</summary>
    /// <param name="parameterName">参数名</param>
    /// <param name="value">参数对象</param>
    protected override void SetParameter(String parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index < 0) throw new ArgumentException(Localization.Translate("Parameter '{0}' was not found.", parameterName), nameof(parameterName));

        SetParameter(index, value);
    }
    #endregion
}
