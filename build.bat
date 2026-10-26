@echo off
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /out:GameRotator.exe /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll GameRotator.cs
