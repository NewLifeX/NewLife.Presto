using System;
using System.Linq;
using System.Reflection;

Console.WriteLine("NewLife.Presto 仓库骨架");
Console.WriteLine("========================");

var asm = Assembly.Load("NewLife.Presto");
Console.WriteLine($"核心程序集：{asm.GetName().Name} v{asm.GetName().Version}");
Console.WriteLine($"依赖：{String.Join(", ", asm.GetReferencedAssemblies().Select(e => e.Name))}");
Console.WriteLine();
Console.WriteLine("当前为仓库脚手架与设计阶段，驱动功能尚未实现。");
Console.WriteLine("功能清单见 Doc/功能清单.md，架构设计见 Doc/架构设计.md。");
