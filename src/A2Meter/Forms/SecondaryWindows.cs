using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace A2Meter.Forms;

internal sealed class SecondaryWindows : IDisposable
{
	private readonly Dictionary<Type, Form> _live = new Dictionary<Type, Form>();

	private readonly Form _owner;

	public SecondaryWindows(Form owner)
	{
		_owner = owner;
	}

	public T Open<T>() where T : Form, new()
	{
		if (_live.TryGetValue(typeof(T), out Form value) && !value.IsDisposed)
		{
			value.Show();
			value.BringToFront();
			value.Activate();
			return (T)value;
		}
		T val = new T();
		_live[typeof(T)] = val;
		val.FormClosed += delegate
		{
			_live.Remove(typeof(T));
		};
		val.Show(_owner);
		return val;
	}

	public void Toggle<T>() where T : Form, new()
	{
		if (_live.TryGetValue(typeof(T), out Form value) && !value.IsDisposed)
		{
			value.Close();
		}
		else
		{
			Open<T>();
		}
	}

	public bool TryGet<T>(out T? form) where T : Form
	{
		if (_live.TryGetValue(typeof(T), out Form value) && !value.IsDisposed)
		{
			form = (T)value;
			return true;
		}
		form = null;
		return false;
	}

	public void CloseAll()
	{
		foreach (Form item in new List<Form>(_live.Values))
		{
			try
			{
				item.Close();
			}
			catch
			{
			}
		}
		_live.Clear();
	}

	public void Dispose()
	{
		CloseAll();
	}
}
