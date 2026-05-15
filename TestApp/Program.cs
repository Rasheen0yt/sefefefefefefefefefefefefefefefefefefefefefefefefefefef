using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;

class Program {
    static void Main() {
        Console.WriteLine("BoxShadow Property exists: " + (ContentPresenter.BoxShadowProperty != null));
    }
}