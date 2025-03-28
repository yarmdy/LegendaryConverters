// See https://aka.ms/new-console-template for more information
using LegendaryConverters;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

Console.WriteLine("Hello, World!");

var converter = new DynamicDicToObjConverter();
Dictionary<string, object?> dic = new Dictionary<string, object?> {
    { "I1","1"},
    { "I2",1L},
    { "B1","true"},
    { "B2",true},
    { "TS1",TimeSpan.FromSeconds(10)},
    { "DT1",DateTime.Now},
    { "Version1","1.0.0.1"},
    { "StringData",new Version("1.0.0.1")},
};
//Convert.ChangeType(1L, typeof(int));
//bool res = TypeDescriptor.GetConverter(typeof(string)).IsValid(dic["StringData"]!);
var obj = converter.Convert<TestClass>(dic);
Console.WriteLine($"{JsonSerializer.Serialize(obj,options:new JsonSerializerOptions {WriteIndented=true })}");

int maxcount = 1000_0000;
Stopwatch sw = Stopwatch.StartNew();
for (int i = 0; i < maxcount; i++)
{
    obj = converter.Convert<TestClass>(dic);
}
sw.Stop();

FormattableString message = $"转换 {maxcount:C} 次，用时 {sw.ElapsedMilliseconds} ms";
Console.WriteLine(message.ToString(CultureInfo.GetCultureInfo("en-US")));

public class TestClass{
    public int I1 { get; set; }
    public int? I2 { get; set; }

    public bool B1 { get; set; }
    public bool? B2 { get; set; }

    public TimeSpan TS1 { get; set; }
    public DateTime DT1 { get; set; }

    public Version? Version1 { get; set; }
    public string? StringData { get; set; }
}