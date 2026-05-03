using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace A2Meter.Forms;

/// Owns at most one live instance of each secondary WebViewForm.
/// Re-opening a window focuses the existing instance instead of creating a duplicate.
internal sealed class SecondaryWindows : IDisposable
{
    private readonly Dictionary<Type, Form> _live = new();
    private readonly Form _owner;

    public SecondaryWindows(Form owner) => _owner = owner;

    public T Open<T>() where T : Form, new()
    {
        if (_live.TryGetValue(typeof(T), out var existing) && !existing.IsDisposed)
        {
            existing.Show();
            existing.BringToFront();
            existing.Activate();
            return (T)existing;
        }

        var form = new T();
        _live[typeof(T)] = form;
        form.FormClosed += (_, _) => _live.Remove(typeof(T));
        form.Show(_owner);
        return form;
    }

    public void Toggle<T>() where T : Form, new()
    {
        if (_live.TryGetValue(typeof(T), out var existing) && !existing.IsDisposed)
            existing.Close();
        else
            Open<T>();
    }

    public bool TryGet<T>(out T? form) where T : Form
    {
        if (_live.TryGetValue(typeof(T), out var f) && !f.IsDisposed)
        {
            form = (T)f;
            return true;
        }
        form = null;
        return false;
    }

    public void CloseAll()
    {
        foreach (var f in new List<Form>(_live.Values))
        {
            try { f.Close(); } catch { }
        }
        _live.Clear();
    }

    public void Dispose() => CloseAll();
}
