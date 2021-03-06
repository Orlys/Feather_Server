﻿using Feather_Server.Database;
using Feather_Server.Entity;
using Feather_Server.Entity.NPC_Related;
using Feather_Server.Entity.PlayerRelated.Items;
using Feather_Server.Entity.PlayerRelated.Items.Activable;
using Feather_Server.MobRelated;
using Feather_Server.Packets;
using Feather_Server.Packets.Actual;
using Feather_Server.PlayerRelated;
using Feather_Server.ServerRelated;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Feather_Server.ServerRelated
{
    public class Client : IDisposable
    {
        bool disposed = false;

        public AES aes;
        public TcpClient tcp;
        public NetworkStream stream;
        public bool isJoined = false; // in game server
        /// <summary>
        /// IP:Port
        /// </summary>
        public string addrInfo;
        public string accountName;  // Login account
        public Hero hero;
        private Thread listenerThread;

        #region idk wt name
        public Client(AES aes, ref TcpClient cli)
        {
            this.aes = aes;
            this.tcp = cli;

            var info = ((IPEndPoint)cli.Client.RemoteEndPoint);
            this.addrInfo = $"{info.Address}:{info.Port}";

            this.stream = tcp.GetStream();
        }

        private void disconnect()
        {
            if (this.tcp != null)
                this.tcp.Close();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                tcp.Close();
                tcp = null;

                if (isJoined)
                {
                    // send despawn to nearbys
                    Lib.sendToNearby(this.hero, PacketEncoder.despawnEntity(this.hero));

                    lock (Lib.entityListByMap)
                    {
                        Lib.entityListByMap.GetValueOrDefault(((IEntity)this.hero).map, null)?.Remove(this.hero);
                    }
                    lock (Lib.clientList)
                    {
                        Lib.clientList.Remove(this.hero.heroID);
                    }
                }

                aes = null;

                if (listenerThread != null)
                    listenerThread = null;
            }

            disposed = true;
        }

        ~Client()
        {
            Dispose(false);
        }
        #endregion

        public void startListen()
        {
            listenerThread = new Thread(delegate ()
            {
                listen();
            });
            listenerThread.Start();
        }

        private void listen()
        {
            byte[] pkt;
            byte[] trimed;
            while (!disposed)
            {
                // REVIEW: add lock for stream?
                SpinWait.SpinUntil(() => disposed || stream.DataAvailable);
                
                if (disposed)
                    return;

                pkt = new byte[1460];
                stream.Read(pkt);
                pkt = aes.decrypt(pkt);
                //Console.WriteLine($"[*] Listener Raw[{Lib.toHex(pkt)}]");

                int lastIdx = Lib.indexOfBytes(pkt, new byte[] { 0x0D, 0x0A });
                if (lastIdx > 0)
                {
                    // text (cmd) pkt
                    trimed = new byte[lastIdx];
                    Buffer.BlockCopy(pkt, 0, trimed, 0, lastIdx);
                    handleCmd(Encoding.ASCII.GetString(trimed));
                }
                else
                {
                    // normal pkt

                    var sz = pkt[0];
                    if (sz == 0xFF && pkt[1] == 0xFF)
                    {
                        // GBK encoded string
                        // TODO: handling... lol
                        trimed = new byte[] { 0x0, 0x0 };
                    }
                    else
                    {
                        // normal format (first byte is size)
                        trimed = new byte[sz];
                        Buffer.BlockCopy(pkt, 0, trimed, 0, sz);
                    }

                    handlePkt(trimed);
                }
            }

        }

        public bool handlePkt(in byte[] recv)
        {
            if (!Lib.isStartWith(recv, new byte[] { 0x0d, 0x0a }))
                Console.WriteLine($"[+] Recv.: [{Lib.toHex(recv)}]");
            return false;
        }

        public bool handleCmd(in string command)
        {
            //Console.WriteLine($"[*] CMD [{command}]");
            var cmd = command.Split('\r')[0].Split(" ");
            
            if (cmd[0].StartsWith("look"))
            {
                // look 6942#
                // look 10245
                if (cmd[1].EndsWith("#"))
                {
                    // talk with NPC
                    // TODO: dialog
                    // TODO: update facing
                }
                else
                {
                    hero.aiming = cmd[1];
                }
                return true;
            }

            if (cmd[0].StartsWith("do"))
            {
                if (cmd[1] == "ride")
                {
                    hero.ride = new Ride();
                    hero.ride.modelID = 0x001b;
                    hero.ride.modelColor = 0x62b9;
                    hero.ride.wingsLv = 0x000b;
                    hero.ride.wingsID = 0x6127;
                    hero.act = 0x04;

                    // sync to all
                    Lib.sendToNearby(hero, PacketEncoder.rideOn(hero), true);

                    // spawn nearby (again, since spawn ride will despawn all existing models) -- also sync the ride models
                    Lib.spawnNearbys(this, hero);
                }
                else if (cmd[1] == "unride")
                {
                    hero.ride = null;

                    // respawn the player
                    Lib.broadcast(HeroPacket.spawnPlayerNormal(hero));

                    // spawn nearby (again, since spawn ride will despawn all existing models) -- also sync the ride models
                    Lib.spawnNearbys(this, hero);
                }
                else if (cmd[1] == "b1")
                {
                    Lib.broadcast(Lib.hexToBytes(@"
                    54690a e3060000 E300 6F00 01 0201 0100 1127 00000000 0201 e780 0c63 00000000000008000000180000000000000000000000000000003d0100000000 0000 0c 00 02 00 0000 bcd3d0fd30352020202020202020202000"
                    //083d01 e3060000 0200 // update entity to ready code: 02
                    // title color & title
                    + @"24a2 14 89240e00 00 9fcb 1000 730e 0000 00 60 2b433078323766353263 602d43 64 1327 000000

                    0d6f e3060000 7c d10c00 04 02 02 00"));
                }
                else if (cmd[1] == "b2")
                {
                    Lib.broadcast(Lib.hexToBytes(@"
                    54690a e3060000 E300 6F00 01 0201 0100 1127 00000000 0201 e780 0c63 00000000000008000000180000000000000000000000000000003d0100000000 0000 19 00 10 00 0000 bcd3d0fd30352020202020202020202000"
                    //083d01 e3060000 0200 // update entity to ready code: 02
                    // title color & title
                    + @"11a2 14 e3060000 1b43b7e7bba8d1a9d4c2 00
                    
                    0d6f e3060000 7d d10c00 04 02 02 00"));
                }
                else if (cmd[1] == "b3")
                {
                    Lib.broadcast(Lib.hexToBytes(@"
                    54690a e3060000 E300 6F00 01 0201 0100 1127 00000000 0201 e780 0c63 00000000000008000000180000000000000000000000000000003d0100000000 0000 25 00 20 00 0000 bcd3d0fd30352020202020202020202000"
                    //083d01 e3060000 0200 // update entity to ready code: 02
                    // title color & title
                    + @"11a2 14 e3060000 1b43b7e7bba8d1a9d4c2 00
                    
                    0d6f e3060000 7e d10c00 04 02 02 00
                    "));
                }
                else if (cmd[1] == "b4")
                {
                    Lib.broadcast(Lib.hexToBytes(@"
                    54690a e3060000 E300 6F00 01 0201 0100 1127 00000000 0201 e780 0c63 00000000000008000000180000000000000000000000000000003d0100000000 0000 31 00 30 00 0000 bcd3d0fd30352020202020202020202000"
                    //083d01 e3060000 0200 // update entity to ready code: 02
                    // title color & title
                    + @"11a2 14 e3060000 1b43b7e7bba8d1a9d4c2 00

                    0d6f e3060000 7f d10c00 04 02 02 00
                    "));
                }
                return true;
            }

            Console.WriteLine($"[*] {command}");

            if (cmd[0].StartsWith("path"))
            {
                //      time- x  y   ??? act
                // path 26903 95,157 210 1 0
                // send to other players.
                // 1da917f68f0000342716002900000039352c3135372032313020312030000000
                ((ILivingEntity)hero).act = byte.Parse(cmd[4]);

                Lib.sendToNearby(hero, PacketEncoder.playerPathSync(hero, cmd[1], $"{cmd[2]} {cmd[3]} {cmd[4]} 0"));
                return true;
            }

            // go2 96,158 4 26903
            // 0f6734271600ae399b125f009d003800
            if (cmd[0].StartsWith("go2"))
            {
                // send to everyone (sender included)
                string[] loc = cmd[1].Split(",");

                ((IEntity)hero).updateLoc(ushort.Parse(loc[0]), ushort.Parse(loc[1]));
                ((IEntity)hero).updateFacing(byte.Parse(cmd[2]));

                Lib.sendToNearby(hero, PacketEncoder.playerLocSync(hero, cmd[3]), true);
                return true;
            }

            if (cmd[0].StartsWith("go"))
            {
                //go >2 -> facing

                ((IEntity)hero).updateFacing(byte.Parse(cmd[1].Substring(1)));
                Lib.sendToNearby(hero, PacketEncoder.updatePlayerFacing(hero), true);
                return true;
            }
            if (cmd[0].StartsWith("act"))
            {
                // act 11 -> triggered by SHIFT + LeftClick
                // TODO: fix bug, idk why the fuck this is not working :(
                // cmd:       6 = stand | 2 = sit | 9 = freeFight | 11 = fight
                // real code: 1 = stand | 4 = sit | 9 = freeFight | _B = fight
                var realAct = byte.Parse(cmd[1]);

                switch (realAct)
                {
                    case 6:
                        hero.act = 1;
                        break;
                    case 2:
                        hero.act = 4;
                        break;
                    case 11:
                        hero.act = 0xB;
                        break;
                    default:
                        hero.act = realAct;
                        break;
                }

                // must add a delay before setting act -- since local server is too quick LOL
                Thread.Sleep(100);
                var pkt = PacketEncoder.playerAct(hero);
                Lib.sendToNearby(hero, pkt, false);

                // confirm act (only for sender)
                PacketEncoder.concatPacket(Lib.hexToBytes(
                    "04" + "3d0901" + "00"
                ), ref pkt, false);
                this.send(pkt);
                return true;
            }

            if (cmd[0].StartsWith("desc"))
            {
                // peek item info:
                // desc 2c15ea#

                if (cmd[1].EndsWith("#"))
                {
                    this.send(PacketEncoder.genItemDesc(
                        this.hero.bag.getItem(uint.Parse(cmd[1][..^1], System.Globalization.NumberStyles.HexNumber))
                    ));
                    return true;
                }

                if (cmd[1] == "char")
                {

                }

                if (cmd[1] == "user")
                {

                }

                if (cmd[1] == "loop")
                {
                    // watch other's CF share damage:
                    //           <PlyerID> <fixed>
                    // desc loop 3063 840000
                    var targetID = int.Parse(cmd[2]);

                    if (cmd[3] == "840000")
                    {
                        // TODO: CF damage reply
                        // __ 84 28e81800 40d10c00 00 40d10c00 64 f4040000 00 # cf 攻擊: 0x04f4
                        var pkt = new byte[0];
                        PacketEncoder.concatPacket(Lib.hexToBytes(
                            "84"
                            + Lib.toHex(targetID)
                            + "40d10c00"
                            + "00"
                            + "40d10c00"
                            + "64"
                            + Lib.toHex(1234567890)
                            + "00"
                        ), ref pkt);

                        this.send(pkt);
                        return true;
                    }
                }

                if (cmd[1] == "skl")
                {
                    // desc skl 510500
                    var skillID = int.Parse(cmd[2]);

                }

                // return false;
            }

            if (cmd[0].StartsWith("pf2"))
            {
                // pf2 
            }

            if (cmd[0].StartsWith("pf6"))
            {
                // skill attack
                //     ..                               time   sk-ID- target
                // pf6 839890f4bbdfea9b8b768aa516c60c92 2228   510010 10123
                // pf6 7fc5a349602da3634be0eff203879419 174236 540010 c1114#
                // pf6 ff1097cb33281622f1bbdafe28896271 192223 540010 54ca#
                // pf6 ff1097cb33281622f1bbdafe28896271 241849 540010         // toward self

                // pkt1
                // player ID: f9d41800
                // __ 5e f9d41800 01 00          // player face 01
                // __ 40 f9d41800 04 28a712 5e 00 // player act 04
                // __ 40 f9d41800 04 28a712 5a 00 // player act 04
                // __ 3d 16 4a010000 00 // update MP
                // __ 50 6a3d0800 ff00 0000 0000 0000 00
                // __ 50 00000000 ff00 0000 0100 0000 00
                // pkt1
                // __ 5e f9d41800 05 00
                // __ 40 f9d41800 50 6ea712 5e 00
                // __ 40 f9d41800 50 6ea712 5a 00
                // __ 3d 16 40010000 00 // update MP
                // __ 50 6a3d0800 ff00 0000 0000 0000 00
                // __ 50 00000000 ff00 0000 0100 0000 00
                // pkt1
                // __ 41 f9d41800 486ea71201000500000000 00
                // __ 5e f9d41800 05 00
                // __ 8a f9d41800 486ea712a9002c00ca54000057d10c000102 00
                // __ 48 ca540000 0800000005 f9d41800 702302 00
                // __ 2a ca540000 02 00
                // __ 50 00000000 ff00 0000 0100 0000 00

                // pkt2
                // __ 8a f9d41800 0628a712b9002c0014110c00 6a3d0800 0102 00
                // __ 6f 14110c00 6a3d0800 010201 00
                // __ 48 14110c00 33000000 05 f9d41800 702302 00
                // __ 2a 14110c00 15 00
                // __ 40 f9d41800 06 28a7125a 00
                // pkt2 | mob entity ID: ca540000 | damage: 0x3F
                // __ 8a f9d41800 486ea712 a9002c00 ca540000 6a3d0800 0102 00
                // __ 6f ca540000 6a3d0800 01 02 01 00
                // __ 48 ca540000 3f000000 05 f9d41800 702302 00
                // __ 2a ca540000 07 00
                // __ 40 f9d41800 486ea712 5a 00
                // pkt2 | mob killed
                // __ 41 f9d41800 4b6ea71201000500000000 00
                // __ 5e f9d41800 05 00
                // __ 8a f9d41800 4b6ea712a9002c00ca54000057d10c000102 00
                // __ 48 ca540000 1300000005 f9d41800 702302 00
                // __ 2a ca540000 00 00
                // __ 2e 06ef1900 a7002c00ee780200000000000000ee780200 00
                // __ a2 04a20110552200e9542200 00
                // __ 3d 2800006d d8000056110000 00
                // __ 78 ca540000 0300 0000 0102 00
                // __ 50 00000000 ff00 0000 0100 0000 00


                // toward self:
                // pkt1:
                // __ 50 00000000 ff00000001000000 00
                // pkt2:
                // __ 50 00000000 ff00000002000000 00
                // __ 3d 16 13010000 00 // update MP
                // __ 40 f9d418003a30a8125e 00
                // __ 40 f9d418003a30a8125a 00
                // __ 50 743d0800 ff00000000000000 00
                // __ 81 743d0800 0807 01 00 // self buff duration
                // __ 6f f9d41800 743d0800010201 00
                // __ 3d 19 280000004b000000 00
                // pkt2:
                // __ 3d 44a900 00
                // __ 21 01490b00 00 00
                // __ 81 bf930800 4042 01 00
                // __ 40 16251a00 e057a8125e 00
                // __ 40 16251a00 e057a8125a 00
                // __ 50 bf930800 ff00000000000000 00
            }

            if (cmd[0].StartsWith("pet"))
            {
                // update player pet mode
                // code: 1: follow atk, 2: follow defense, 3: follow passive
                //       4:   hold atk, 5:   hold defense, 6:   hold passive

                //     eid---       code
                // pet 3384c8# mode 2

            }

            if (cmd[0].StartsWith("key"))
            {
                // update itembar binding:
                //     slot skillID
                // key 7    570090 // bind
                // key 5    0      // remove
                //          itemName- backpackSlotIndex
                // key 6    顶级灵月液 4 // bind backpack item
            }

            if (cmd[0].StartsWith("kill2"))
            {
                // melee normal attack
                //       skill hash..?                    time target
                // kill2 a8b679be12561dd3c9049096bd8feaf3 217 10123
            }

            if (cmd[0].StartsWith("gift"))
            {
                // maunally level up
                // cmd: gift upgrade
                if (cmd[1] == "upgrade")
                {
                    // [0]
                    // __ 2f 8c8c2100 00
                    // [1]
                    // __ 3d 2c040035010000 00
                    // __ 3d a400002500000064000000000000000c0004009c000000350100000000000000000000 00
                    // __ 3d 2b1000 00
                    // __ ad 0000000000 00
                    // __ 3d 0a1b00 00
                    // __ 3d 0e1b00 00
                    // __ 3d 0b1b00 00
                    // __ 3d 0c1b00 00
                    // __ 3d 0d1b00 00
                    // __ 3d 1567010000 00
                    // __ 2a 2584210030 00
                    // __ 3d 1773000000 00
                    // __ 3d 1b0c0000002f000000 00
                    // __ 3d 181300000036000000 00
                    // __ 3d 191c0000003f000000 00
                    // __ 3d f70a000000 00
                    // __ 3d 1d64000000 00
                    // __ 3d f604000000 00
                    // __ 3d 1467010000 00
                    // __ 2a 2584210032 00
                    // __ 3d 1673000000 00
                    // __ 6f 25842100215c0c000202215c0c00020201 00
                    // __ 21 01570f00006404000000 00
                }
            }

            if (cmd[0].StartsWith("learn"))
            {
                // learn skill
                // learn !! 510000  // role basic skill
                // learn !!! 510000 // role advance skill
                // reply:
                /*
                 
                 */
            }

            if (cmd[0].StartsWith("tasklog"))
            {
                // TODO: make it works..
                //this.send(new byte[]
                //{
                //    0x08, 0x3d, 0x01, 0xc8, 0x64, 0x0b, 0x00, 0x02, 0x00, 0x04, 0xa9, 0x1a, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x04, 0xc1, 0xf2, 0x00, 0x00, 0x64, 0x00, 0x01, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1d, 0x16, 0x00, 0x00, 0x64, 0x7a, 0x9c, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0xed, 0x14, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0x52, 0x00, 0x00, 0x00, 0x64, 0xa2, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x04, 0xbe, 0xf2, 0x00, 0x00, 0x50, 0x00, 0x01, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1d, 0x16, 0x00, 0x00, 0x64, 0x50, 0x9c, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0xed, 0x14, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0x52, 0x00, 0x00, 0x00, 0x64, 0xa2, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x8c, 0xf2, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0xd3, 0x15, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0x93, 0x16, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xd1, 0x00, 0x00, 0x00, 0x64, 0xb1, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x80, 0xee, 0x00, 0x00, 0x28, 0x00, 0xff, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1d, 0x16, 0x00, 0x00, 0x64, 0x70, 0x13, 0x00, 0x00, 0x64, 0xd1, 0x15, 0x00, 0x00, 0x64, 0xb7, 0x13, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0x89, 0x00, 0x00, 0x00, 0x64, 0xd4, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x04, 0x85, 0xee, 0x00, 0x00, 0x32, 0x00, 0x0a, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1d, 0x16, 0x00, 0x00, 0x64, 0xdf, 0x15, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0x4d, 0x15, 0x00, 0x00, 0x64, 0xf7, 0x01, 0x00, 0x00, 0x64, 0xca, 0x00, 0x00, 0x00, 0x64, 0xb4, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x84, 0xee, 0x00, 0x00, 0x1e, 0x00, 0x0a, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1d, 0x16, 0x00, 0x00, 0x64, 0xd0, 0x15, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0xc2, 0x14, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xf6, 0x00, 0x00, 0x00, 0x64, 0x7f, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x83, 0xee, 0x00, 0x00, 0x14, 0x00, 0x0a, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1d, 0x16, 0x00, 0x00, 0x64, 0xcf, 0x15, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0xc9, 0x13, 0x00, 0x00, 0x64, 0xc9, 0x00, 0x00, 0x00, 0x64, 0xbe, 0x00, 0x00, 0x00, 0x64, 0x80, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x81, 0xee, 0x00, 0x00, 0x0e, 0x00, 0x0a, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1d, 0x16, 0x00, 0x00, 0x64, 0xce, 0x15, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0x05, 0x14, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xdc, 0x00, 0x00, 0x00, 0x64, 0x6b, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x76, 0xee, 0x00, 0x00, 0x00, 0x00, 0xff, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1c, 0x16, 0x00, 0x00, 0x64, 0xd4, 0x15, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0x94, 0x16, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xb2, 0x00, 0x00, 0x00, 0x64, 0x98, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x7e, 0xee, 0x00, 0x00, 0x19, 0x00, 0x06, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0xdb, 0x15, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0xdc, 0x15, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xcc, 0x00, 0x00, 0x00, 0x64, 0x96, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x68, 0xee, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0xaa, 0x15, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0xdc, 0x15, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xcc, 0x00, 0x00, 0x00, 0x64, 0x96, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x04, 0x67, 0xee, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1d, 0x16, 0x00, 0x00, 0x64, 0x60, 0x22, 0x00, 0x00, 0x64, 0x1a, 0x16, 0x00, 0x00, 0x64, 0xc2, 0x14, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xf6, 0x00, 0x00, 0x00, 0x64, 0x7f, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x6b, 0xee, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0x80, 0x15, 0x00, 0x00, 0x64, 0xad, 0x16, 0x00, 0x00, 0x64, 0xac, 0x16, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xf6, 0x00, 0x00, 0x00, 0x64, 0x7f, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x66, 0xf2, 0x00, 0x00, 0x1e, 0x00, 0x09, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0xfe, 0x21, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0xaa, 0x16, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0x92, 0x00, 0x00, 0x00, 0x64, 0x61, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x04, 0x5d, 0xee, 0x00, 0x00, 0x3c, 0x00, 0x01, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0x42, 0x1f, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0xe4, 0x14, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0x8f, 0x00, 0x00, 0x00, 0x64, 0xd9, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x43, 0xf2, 0x00, 0x00, 0x0f, 0x00, 0x02, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0xfc, 0x11, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0xb7, 0x13, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0x89, 0x00, 0x00, 0x00, 0x64, 0xd4, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x45, 0xf2, 0x00, 0x00, 0x05, 0x00, 0xff, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0xa2, 0x0f, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0x93, 0x13, 0x00, 0x00, 0x64, 0x65, 0x00, 0x00, 0x00, 0x64, 0xc0, 0x00, 0x00, 0x00, 0x64, 0x97, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x04, 0x3c, 0xf2, 0x00, 0x00, 0x37, 0x00, 0x05, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1d, 0x16, 0x00, 0x00, 0x64, 0x17, 0x16, 0x00, 0x00, 0x64, 0x19, 0x16, 0x00, 0x00, 0x64, 0x18, 0x16, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0x9d, 0x00, 0x00, 0x00, 0x64, 0xc0, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x3b, 0xf2, 0x00, 0x00, 0x28, 0x00, 0xff, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1d, 0x16, 0x00, 0x00, 0x64, 0x14, 0x16, 0x00, 0x00, 0x64, 0x16, 0x16, 0x00, 0x00, 0x64, 0x15, 0x16, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x3e, 0xf2, 0x00, 0x00, 0x14, 0x00, 0x01, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0x5a, 0x1f, 0x00, 0x00, 0x64, 0x0c, 0x16, 0x00, 0x00, 0x64, 0x0d, 0x16, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xd4, 0x00, 0x00, 0x00, 0x64, 0x81, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x27, 0xf6, 0x00, 0x00, 0x1e, 0x00, 0x0a, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1d, 0x16, 0x00, 0x00, 0x64, 0xfd, 0x21, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0x0f, 0x14, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xd8, 0x00, 0x00, 0x00, 0x64, 0xca, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x04, 0x26, 0xf6, 0x00, 0x00, 0x0a, 0x00, 0x01, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x00, 0xb4, 0x0a, 0x00, 0x64, 0x13, 0x16, 0x00, 0x00, 0x64, 0x01, 0xb4, 0x0a, 0x00, 0x64, 0x12, 0x16, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xb2, 0x00, 0x00, 0x00, 0x64, 0x98, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x04, 0x25, 0xf6, 0x00, 0x00, 0x0a, 0x00, 0x05, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1c, 0x16, 0x00, 0x00, 0x64, 0x11, 0x16, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0x12, 0x16, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xb2, 0x00, 0x00, 0x00, 0x64, 0x98, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x24, 0xf6, 0x00, 0x00, 0x1e, 0x00, 0x0f, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0x49, 0x1f, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0xda, 0x13, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xa6, 0x00, 0x00, 0x00, 0x64, 0xa1, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x23, 0xf6, 0x00, 0x00, 0x14, 0x00, 0x14, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0x54, 0x1f, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0xe3, 0x13, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0x05, 0x01, 0x00, 0x00, 0x64, 0x88, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x22, 0xf6, 0x00, 0x00, 0x0a, 0x00, 0x1e, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0x55, 0x1f, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0x09, 0x16, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0x99, 0x00, 0x00, 0x00, 0x64, 0x60, 0x00, 0x00, 0x00, 0x00, 0x33, 0xa9, 0x1a, 0x01, 0x11, 0xfa, 0x00, 0x00, 0x23, 0x00, 0x08, 0x00, 0x1f, 0x16, 0x00, 0x00, 0x64, 0x1b, 0x16, 0x00, 0x00, 0x64, 0x0a, 0x16, 0x00, 0x00, 0x64, 0x08, 0x16, 0x00, 0x00, 0x64, 0x0b, 0x16, 0x00, 0x00, 0x64, 0x68, 0x00, 0x00, 0x00, 0x64, 0xd5, 0x00, 0x00, 0x00, 0x64, 0xb7, 0x00, 0x00, 0x00, 0x00, 0x04, 0xa9, 0x1a, 0x09, 0x00, 0x00,
                //});
                //this.send(new byte[]
                //{
                //    0x0c, 0x40, 0xc8, 0x64, 0x0b, 0x00, 0x46, 0xd2, 0x79, 0x12, 0x5e, 0x01, 0x00, 0x00, 0x00, 0x00,
                //});
                return true;
            }

            if (cmd[0] == "ride")
            {
                if (cmd[1] == "list")
                {
                    for (byte i = 1; i <= hero.rideList.Count; i++)
                        this.send(PacketEncoder.rideItem(hero, i));
                }
                else if (cmd[1] == "desc")
                {
                    var idx = byte.Parse(cmd[2]);
                    this.send(PacketEncoder.rideItem(hero, idx));
                }
                else if (cmd[1] == "show")
                {
                    var idx = byte.Parse(cmd[2]);

                    hero.ride = hero.rideList[idx - 1];

                    // broadcast to nearbys
                    Lib.sendToNearby(hero, PacketEncoder.rideOn(hero), true);

                    Lib.spawnNearbys(this, hero);

                    // active the ride
                    this.send(PacketEncoder.rideItem(hero, idx));

                    // TODO: ride stat share calculation & update player stat (include attack, defense, dodge, etc.)

                    // update ride 
                    //var source = new CancellationTokenSource();
                    //Task.Run(async delegate
                    //{
                    //    // TODO: delay & progress bar for ride show
                    //    //await Task.Delay(TimeSpan.FromSeconds(5), source.Token);

                    //    //this.send(PacketEncoder.progressBarComplete(true));
                    //    hero.ride = hero.rideList[idx];

                    //    // broadcast to nearbys
                    //    Lib.sendToNearby(hero, PacketEncoder.rideOn(hero), true);

                    //    source.Dispose();
                    //});
                }
                else if (cmd[1] == "hide")
                {
                    if (hero.ride == null)
                        return true;

                    byte idx = (byte)(hero.rideList.FindIndex(ride => ride.descItemID == hero.ride.descItemID) + 1);

                    hero.ride = null;

                    // respawn the player
                    Lib.sendToNearby(hero, HeroPacket.spawnPlayerNormal(hero).ToArray(), true);

                    // also send de-activate ride item (must after spawn player)
                    this.send(PacketEncoder.rideItem(hero,
                        idx
                    ));

                    // spawn nearby (again, since spawn ride will despawn all existing models) -- also sync the ride models
                    Lib.spawnNearbys(this, hero);
                }

                return true;
            }

            if (cmd[0].StartsWith("remove"))
            {
                // remove <itemUID>

            }

            if (cmd[0] == "save")
            {
                this.save();
                return true;
            }

            if (cmd[0].StartsWith("use"))
            {
                // use <itemUID>
                if (cmd[1].EndsWith("#"))
                {
                    byte[] pkts;
                    Item item = this.hero.bag.getItem(uint.Parse(cmd[1][..^1], System.Globalization.NumberStyles.HexNumber));

                    if (item is RideWing)
                        pkts = ((RideWing)item).use(this.hero);
                    else if (item is RideContract)
                        pkts = ((RideContract)item).use(this.hero);
                    else
                        pkts = item.use(this.hero);

                    this.send(pkts);
                }
            }

            if (cmd[0].StartsWith("teacher"))
            {
                // teacher mytongmen
                // teacher baishi 10123
                // teacher mytudi
                // teacher shoutu 10123

            }

            if (cmd[0].StartsWith("quit_view"))
            {
                this.send(new byte[]
                {
                    0x08, 0xa9, 0x29, 0x04, 0x0d, 0x0a, 0x0d, 0x0a, 0x00, 0x04, 0xa9, 0x29, 0x05, 0x00, 0x08, 0xa9, 0x29, 0x02, 0xbc, 0x05, 0x00, 0x00, 0x00, 0x04, 0xa9, 0x29, 0x00, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0xd3, 0x15, 0x00, 0x00, 0x00, 0x05, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0x70, 0x13, 0x00, 0x00, 0x00, 0xff, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0xd0, 0x15, 0x00, 0x00, 0x00, 0x0a, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0xcf, 0x15, 0x00, 0x00, 0x00, 0x0a, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0xce, 0x15, 0x00, 0x00, 0x00, 0x0a, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0xd4, 0x15, 0x00, 0x00, 0x00, 0xff, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0xdb, 0x15, 0x00, 0x00, 0x00, 0x06, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0xaa, 0x15, 0x00, 0x00, 0x00, 0x64, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0x60, 0x22, 0x00, 0x00, 0x00, 0x06, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0x80, 0x15, 0x00, 0x00, 0x00, 0x01, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0xfe, 0x21, 0x00, 0x00, 0x00, 0x09, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0xfc, 0x11, 0x00, 0x00, 0x00, 0x02, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0xa2, 0x0f, 0x00, 0x00, 0x00, 0xff, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0x14, 0x16, 0x00, 0x00, 0x00, 0xff, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0x5a, 0x1f, 0x00, 0x00, 0x00, 0x01, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0xfd, 0x21, 0x00, 0x00, 0x00, 0x0a, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0x13, 0x16, 0x00, 0x00, 0x00, 0x01, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0x11, 0x16, 0x00, 0x00, 0x00, 0x05, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0x49, 0x1f, 0x00, 0x00, 0x00, 0x0f, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0x54, 0x1f, 0x00, 0x00, 0x00, 0x14, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0x55, 0x1f, 0x00, 0x00, 0x00, 0x1e, 0x00, 0x0a, 0xa9, 0x29, 0x06, 0x0a, 0x16, 0x00, 0x00, 0x00, 0x08, 0x00, 0x04, 0xa9, 0x29, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                });
                return true;
            }

            if (cmd[0].StartsWith("quit"))
            {
                Console.WriteLine($"[!] Client[{addrInfo}] Quit Request.");
                this.Dispose();
                return true;
            }

            Console.WriteLine($"[?] new cmd [{command}]");
            // TODO: Logger.log(LOGLEVEL.LOG | LOGLEVEL.CMD | LOGLEVEL.FILE, /*string*/cmd);
            return false;
        }

        public void save()
        {
            DB2.GetInstance().Update(
                "Hero",
                new Dictionary<string, object>()
                {
                    //{ "basicInfo", this.hero.basicToJson() }, // TODO: save the updated basicInfo
                    { "fullInfo", this.hero.toJson() }
                },
                "heroID = @heroID",
                new Dictionary<string, object>()
                {
                    { "heroID", (int)this.hero.heroID }
                }
            );
        }

        public void send(PacketStreamData packets)
        {
            send(packets.ToArray());
        }

        public void send(byte[] packets)
        {
            if (packets == null)
                return;
            var blocks = Lib.splitBlock(aes.encrypt(packets));
            blocks.ForEach((block) => stream.Write(block));
        }

        public byte[] read(int size = 1460)
        {
            var pkt = new byte[size];
            this.stream.Read(pkt);
            return aes.decrypt(pkt);
        }
    }
}
