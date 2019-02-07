using NUnit.Framework;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets.Kcp;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NUnit.TestsKCP
{
    public class Handle : IKcpCallback
    {
        public void Output(ReadOnlySpan<byte> buffer)
        {
            var frag = new byte[buffer.Length];
            buffer.CopyTo(frag);
            Out(frag);
        }

        public Action<byte[]> Out;
        public Action<IMemoryOwner<byte>> Recv;
        public void Receive(IMemoryOwner<byte> buffer)
        {
            Recv(buffer);
        }

        public IMemoryOwner<byte> RentBuffer(int lenght)
        {
            return null;
        }
    }

    [TestFixture]
    public class TestClass
    {
        public const string message =
        #region MyRegion

            @"LICENSE SYSTEM [2017918 10:58:53] Next license update check is after 2025-06-30T00:00:00

Built from '5.5/release' branch; Version is '5.5.0f3 (38b4efef76f0) revision 3716335'; Using compiler version '160040219'
OS: 'Windows 7 Service Pack 1 (6.1.7601) 64bit' Language: 'zh' Physical Memory: 16224 MB
BatchMode: 0, IsHumanControllingUs: 1, StartBugReporterOnCrash: 1, Is64bit: 1, IsPro: 1
Initialize mono
Mono path[0] = 'C:/Program Files/Unity5.5.0/Editor/Data/Managed'
Mono path[1] = 'C:/Program Files/Unity5.5.0/Editor/Data/Mono/lib/mono/2.0'
Mono path[2] = 'C:/Program Files/Unity5.5.0/Editor/Data/UnityScript'
Mono config path = 'C:/Program Files/Unity5.5.0/Editor/Data/Mono/etc'
Using monoOptions --debugger-agent=transport=dt_socket,embedding=1,defer=y,address=0.0.0.0:56392
IsTimeToCheckForNewEditor: Update time 1505705705 current 1505703540
C:/work/irobotqv2.0_dev/irobotqv2.0_app
Loading GUID <-> Path mappings...0.000281 seconds
Loading Asset Database...0.015599 seconds
Audio: FMOD Profiler initialized on port 54900
AssetDatabase consistency checks...0.019115 seconds
Initialize engine version: 5.5.0f3 (38b4efef76f0)
GfxDevice: creating device client; threaded=1
Direct3D:
    Version:  Direct3D 11.0 [level 11.0]
    Renderer: AMD Radeon HD 6670 (ID=0x6758)
    Vendor:   ATI
    VRAM:     4418 MB
    Driver:   14.100.0.0
Begin MonoManager ReloadAssembly1
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEngine.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEditor.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.Locator.dll (this message is harmless)
Refreshing native plugins compatible for Editor in 9.17 ms, found 3 plugins.
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.CJK.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.DataContract.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Core.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.IvyParser.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Xml.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Configuration.dll (this message is harmless)
Begin MonoManager ReloadAssembly2
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEngine.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEditor.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.Locator.dll (this message is harmless)
Refreshing native plugins compatible for Editor in 9.17 ms, found 3 plugins.
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.CJK.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.DataContract.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Core.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.IvyParser.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Xml.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Configuration.dll (this message is harmless)
Begin MonoManager ReloadAssembly3
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEngine.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEditor.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.Locator.dll (this message is harmless)
Refreshing native plugins compatible for Editor in 9.17 ms, found 3 plugins.
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.CJK.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.DataContract.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Core.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.IvyParser.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Xml.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Configuration.dll (this message is harmless)
Begin MonoManager ReloadAssembly4
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEngine.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEditor.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.Locator.dll (this message is harmless)
Refreshing native plugins compatible for Editor in 9.17 ms, found 3 plugins.
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.CJK.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.DataContract.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Core.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.IvyParser.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Xml.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Configuration.dll (this message is harmless)
Begin MonoManager ReloadAssembly5
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEngine.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEditor.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.Locator.dll (this message is harmless)
Refreshing native plugins compatible for Editor in 9.17 ms, found 3 plugins.
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.CJK.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.DataContract.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Core.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.IvyParser.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Xml.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Configuration.dll (this message is harmless)";

        #endregion

        [Test]
        public void TestKCP()
        {
            ///发送次数
            const int echoTimes = 3;
            Random random = new Random();

            var handle1 = new Handle();
            var handle2 = new Handle();

            const int conv = 123;
            var kcp1 = new Kcp(conv, handle1);
            var kcp2 = new Kcp(conv, handle2);

            ///kcp设置
            ///https://github.com/skywind3000/kcp/issues/39#issuecomment-244592173
            kcp1.NoDelay(1, 10, 2, 1);//fast
            kcp1.WndSize(64, 64);
            kcp1.SetMtu(512);

            kcp2.NoDelay(1, 10, 2, 1);//fast
            kcp2.WndSize(64, 64);
            kcp2.SetMtu(512);

            var sendbyte = Encoding.ASCII.GetBytes(message);

            handle1.Out += buffer =>
            {
                var next = random.Next(100);
                if (next >= 5)///随机丢包
                {
                    kcp2.Input(buffer); 
                }
            };

            handle2.Out += buffer =>
            {
                 kcp1.Input(buffer);
            };

            int end = 0;
            handle1.Recv += buffer =>
            {
                unsafe
                {
                    using (MemoryHandle p = buffer.Memory.Pin())
                    {
                        var str = Encoding.ASCII.GetString((byte*)p.Pointer, buffer.Memory.Length);
                        Assert.AreEqual(message, str);
                        Interlocked.Increment(ref end);
                        //Assert.Warn($"echo {end}");
                        if (end < echoTimes)
                        {
                            kcp1.Send(buffer.Memory.Span);
                        }
                    }
                    
                }
            };

            handle2.Recv += buffer =>
            {
                kcp2.Send(buffer.Memory.Span);
            };

            Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        kcp1.Update(DateTime.UtcNow);

                        await Task.Delay(5);
                    }
                }
                catch (Exception e)
                {
                    e.ToString();
                }
                
            });

            Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        kcp2.Update(DateTime.UtcNow);

                        await Task.Delay(5);
                    }
                }
                catch (Exception e)
                {
                    e.ToString();
                }
            });

            kcp1.Send(sendbyte);

            var task = Task.Run(async () =>
            {
                while (end < echoTimes)
                {
                    await Task.Yield();
                }
                return 1;
            });

            task.Result.ToString();
        }
    }
}
