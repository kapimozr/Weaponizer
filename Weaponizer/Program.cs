using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZCompressLibrary;

namespace Weaponizer
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Enter the seed name too.");
                return;
            }
            
            //todo better (or any lol) file checking
            string fileName = args[0];
            //string fileName = "test0.sfc";

            byte[] data = File.ReadAllBytes(fileName);
            int table_address = 0x27ce0;
            int c_table_length = 799;
            Random rnd = new Random();
            string spoiler = "";

            // decompress the enemy damage table from the ROM (thanks Zarby)
            int f = 0;
            byte[] sdata = Decompress.ALTTPDecompressOverworld(data, 0x27ce0, c_table_length, ref f);
            byte[] udata = new byte[3000];
            byte[] enemy_table = new byte[5000];

            // expanding the array (unsure if needed)
            for (int i = 0; i < udata.Length; i++)
            {
                if (i < sdata.Length)
                {
                    udata[i] = sdata[i];
                }
                else
                {
                    udata[i] = 0;
                }
            }

            // split bytes
            for (int i = 0; i < 5000; i += 2)
            {
                enemy_table[i] = (byte)(udata[i / 2] >> 4);
                enemy_table[i + 1] = (byte)(udata[i / 2] & 0x0F);
            }

            // normalize the enemy damage table
            byte[] new_enemy_table = Enemy_Table_Setup(enemy_table);

            // recombine bytes
            byte[] combined_table = new byte[2048];
            for (int i = 0; i < 2048; i += 1)
            {
                combined_table[i] = (byte)((new_enemy_table[(i * 2)] << 4) | new_enemy_table[(i * 2) + 1]);
            }
            // recompress table
            byte[] compressed_enemy_table = Compress.ALTTPCompressOverworld(combined_table, 0, combined_table.Length);

            // write table to ROM data - table cannot exceed 799 bytes length
            for (int i = 0; (i < c_table_length & i < compressed_enemy_table.Length); i++)
            {
                data[table_address + i] = compressed_enemy_table[i];
            }

            // create new damage table and write to ROM data
            byte[] new_damage_table = Create_Damage_Table();
            int damage_table_address = 0x6B8F1;
            for (int i = 0; i < 128; i++)
            {
                data[damage_table_address + i] = new_damage_table[i];
            }
            // write spoiler info
            string[] weaponClassNames = new string[] { "Boomerang", "Level 1", "Level 2", "Level 3", "Level 4", "Level 5",
                "Bow", "Hookshot", "Bombs", "Silvers", "Powder", "Fire Rod", "Ice Rod", "Bombos", "Ether", "Quake" };
            for (int i = 0; i < 16; i++)
            {
                spoiler += weaponClassNames[i] + ": " + Convert.ToString(new_damage_table[i*8 + 1]) + "\r\n";
            }

            // randomize powder fairy prize
            int fairy_address = 0x36DD0;    // fairy, bees, appl, fish, heart, $5, $20, bomb, magic
            byte[] fairy_options = new byte[] { 0xE3, 0x79, 0xAC, 0xD2, 0xD8, 0xDA, 0xDB, 0xDC, 0xDF };
            data[fairy_address] = fairy_options[rnd.Next(fairy_options.Length)];
            spoiler += "Fairy prize: " + Convert.ToString(data[fairy_address] + "\r\n");

            // randomize bomb timers lol
            int bomb_timer_address = 0x41543;
            // write fuse timer to ROM, first byte - setting to 00 causes max timer 
            data[bomb_timer_address] = (byte)(rnd.Next(254) + 1);
            spoiler += "Bomb timers: " + Convert.ToString(data[bomb_timer_address]) + ", ";
            // bomb explosion speed is next 10 bytes
            byte[,] rates = new byte[,] {
            { 0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x02, 0x02, 0x02 }, // fastest
            { 0x03, 0x02, 0x02, 0x02, 0x02, 0x02, 0x03, 0x03, 0x03, 0x03 }, // double speed
            { 0x06, 0x04, 0x04, 0x04, 0x04, 0x04, 0x06, 0x06, 0x06, 0x06 }, // default
            { 0x0C, 0x08, 0x08, 0x08, 0x08, 0x08, 0x0C, 0x0C, 0x0C, 0x0C }, // half speed
            { 0x18, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x18, 0x18, 0x18, 0x18 }, // quarter speed lol
            // repeat 'moderate' ones to increase weight
            { 0x03, 0x02, 0x02, 0x02, 0x02, 0x02, 0x03, 0x03, 0x03, 0x03 }, // double speed
            { 0x06, 0x04, 0x04, 0x04, 0x04, 0x04, 0x06, 0x06, 0x06, 0x06 }, // default
            { 0x0C, 0x08, 0x08, 0x08, 0x08, 0x08, 0x0C, 0x0C, 0x0C, 0x0C }, // half speed
            // repeat default again for most weight
            { 0x06, 0x04, 0x04, 0x04, 0x04, 0x04, 0x06, 0x06, 0x06, 0x06 } }; // default

            // choose one of the 9 rows
            int row = rnd.Next(9);
            // write to ROM
            for (int i = 0; i<10; i++)
            {
                data[bomb_timer_address + i + 1] = rates[row, i];
                spoiler += Convert.ToString(rates[row, i]) + " ";
            }
            spoiler += "\r\n";

            File.WriteAllText("wpn_" + fileName + ".txt", spoiler);

            FileStream fs = new FileStream("wpn_"+fileName, FileMode.CreateNew, FileAccess.Write);
            fs.Write(data, 0, data.Length);
            fs.Close();
            

        }

        /* 'normalizes' the enemy damage class table
         * @param table byte[] the enemy damage class table extracted from the ROM
         * @return byte[] the new modified table
         */
        static byte[] Enemy_Table_Setup(byte[] table)
        {
            /*  Brand new damage table rules brought to you by ya boi kapimozr
             *  class 0 = no damage (this is default)
             *  class 1 = 'regular' damage (this is what most enemies take). This will be used for as many
             *      enemies as possible, to normalize the damage/effects by weapons.
             *  class 2 = 'safe' damage (this is different). This will be used for bosses so they don't get
             *      hit with effects that delete them and cause a softlock.
             *  class 3 = not used but reserved
             *  class 4 = fire rod & bombos special category (fire rod / bombos class 4 damage is set in the
             *      damage table randomization) needed for freezors and kholdstare shell
             *  class 5 = not used
             *  class 6 = not used
             *  class 7 = not used
             */


            // these are things we don't want to set to use damage class 1, which may be volatile
            int[] normalDmgExceptions = new int[]
            {
                3, 4, 5, 6, 7, // switches and stuff
                9, // moldorm
                11, // chicken
                20, // idk
                21, // antifairy
                22, 26, 28, 29, 30, 31, 33, 37, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, // idk
                51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 63, // still idk
                // 64 is the electrical barrier, don't care for now
                82, // idk
                83, // armos knight
                84, // lanmolas
                87, 89, 90, // idk
                91, 92, 93, 94, 95, 96, 97, // sparks, rollers, a beamos
                98, 99, 101, 102, 103, 104, 105, 108, // idk
                112, // helmasaur fireball
                114, 115, 116, 117, 118, 120, // idk
                119, // another anti fairie or something
                122, // aghanim
                123, // aghanim ball
                125, 126, 127, 128,  // spike, firebars, firesnake
                130, // more antifairies
                135, // a fireball
                136, 137, // mothula, mothula beam
                138, // spike block
                140, // arrghus
                146, // helmasaur
                147, // idk
                149, 150, 151, 152, // eye lasers
                158, 159, 160, // idk
                162, // kholdstare
                171, 172, 173, 174, 175, 176, 177, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, // idk
                189, // vitreous
                191, // aghanim lightning
                192, 193, // idk
                194, // boulder
                196, 197, 198, 200, // idk
                203, // trinexx
                206, // blind
                210, 213, // idk
                212, // a mine
                214, 215 // ganon
            };

            // we want bosses to take damage class 2 (well, I want them to)
            int[] bosses = new int[] 
            {
                83, // armos
                84, // lanmolas
                9, // moldorm
                122, // aghanim - probably shouldn't be here but it's funny
                146, // helmasaur
                140, // arrghus
                136, 137, // mothula, mothula beam
                206, // blind
                162, // kholdstare
                189, 190, // vitreous, eyeballs
                203, // trinexx
                214, 215 // ganon
            };


            // now to actually change the table
            int address;
            // each row is an enemy
            for (int enemyID=0; enemyID<216; enemyID++)
            {
                // kholdstare's shell and freezors must always take damage from fire rod and bombos because the game expects it
                if (enemyID == 163 || enemyID == 161)
                {
                    for (int weaponClass = 0; weaponClass < 16; weaponClass++)
                    {
                        address = weaponClass + enemyID * 16;
                        // 11 = fire rod ; 13 = bombos
                        if (weaponClass == 11 || weaponClass == 13)
                        {
                            table[address] = 4;
                        }
                        else
                        {
                            // just give it 'safe' damage for other weapons and let the player screw around
                            table[address] = 2;
                        }
                    }
                    continue;
                }

                // if enemy is not in exception list, give it class 1 damage for all weapons
                if (!Array.Exists(normalDmgExceptions, element => element == enemyID))
                {
                    for (int weaponClass = 0; weaponClass < 16; weaponClass++)
                    {
                        address = weaponClass + enemyID * 16;
                        table[address] = 1;
                    }
                    continue;
                }

                // if it's a boss, assign it damage class 2 (the 'safe' damage class)
                // also slimes need to not take slime damage so lets just make them safe
                if (Array.Exists(bosses, element => element == enemyID) || enemyID == 143)
                {
                    for (int weaponClass = 0; weaponClass < 16; weaponClass++)
                    {
                        address = weaponClass + enemyID * 16;
                        table[address] = 2;
                    }
                    continue;
                }
                // anything that misses all conditions has either default all-zeros (takes no damage)...
                // ...or does not have a sprite. if you mess with these you deserve whatever happens.
            }
            return table;
        }

        /* make a brand new damage table
         * @return byte[128] a new damage table
         */
        static byte[] Create_Damage_Table()
        {
            Random random = new Random();
            int rnd; // from old method

            // all possible damage values, and corresponding 'safe' values. length 13
            // damage*5, fairy, stun--, stun-, incinerate, freeze, stun, slime
            byte[] damages = new byte[] {0x01,0x02,0x04,0x08,0x10,0x64,0xF9,0xFB,0xFC,0xFD,0xFE,0xFF,0xFA};
            byte[] safeDmg = new byte[] {0x01,0x02,0x04,0x08,0x10,0x64,0x08,0x01,0x01,0x64,0x04,0x02,0x08};

            // new concept: 16 values for 1:1 to the weapon classes, no "repeats" (some intentional). length 16, last 3 are new
            byte[] full_damages = new byte[] { 0x01, 0x02, 0x04, 0x08, 0x10, 0x64, 0xF9, 0xFB, 0xFC, 0xFD, 0xFE, 0xFF, 0xFA, 0x04, 0x08, 0xF9};
            byte[] full_safeDmg = new byte[] { 0x01, 0x02, 0x04, 0x08, 0x10, 0x64, 0x08, 0x01, 0x01, 0x64, 0x04, 0x02, 0x08, 0x04, 0x08, 0x08};

            // int 0-15 randomly ordered, used to randomly choose damage without unwanted repeats
            int[] rndIndex = Enumerable.Range(0, 16).OrderBy(r => random.Next()).ToArray();
            byte[] table = new byte[128];


            // there are 16 different weapon classes (rows)
            for (int weaponClass = 0; weaponClass < 16; weaponClass++)
            {
                // initializing all bytes to zero (instead of null) because most will be zero anyway
                for (int damageClass = 0; damageClass < 8; damageClass++)
                {
                    table[weaponClass * 8 + damageClass] = 0;
                }

                // damage class 0 always stays 00
                // damage class 1 is random
                table[weaponClass * 8 + 1] = full_damages[rndIndex[weaponClass]];

                // OLD METHOD // //
                // if it is a sword, it CANNOT include slime (index 12) because slashing the slime again will softlock
                //if (1 <= weaponClass & weaponClass <= 5)
                //{
                //    table[weaponClass * 8 + 1] = damages[random.Next(11)];
                //}
                //else
                //{
                //     choose from random ordered list
                //    table[weaponClass * 8 + 1] = full_damages[rndIndex[weaponClass]];
                //}
                // // // // // // //


                // damage class 2 is a 'safe' equivalent
                table[weaponClass * 8 + 2] = full_safeDmg[rndIndex[weaponClass]];

                // WEAPON class 2 will always be damaging to avoid softlocks
                if (weaponClass == 2)
                {
                    table[weaponClass * 8 + 1] = full_safeDmg[rndIndex[weaponClass]];
                }
                // damage class 4 for fire rod and bombos, which must be adequately damaging against kholdstare's shell
                else if (weaponClass == 11) // fire rod
                {
                    // fire rod gets random bonus damage, takes 2-8 hits to break shell
                    table[weaponClass * 8 + 4] = (byte)(8+random.Next(24));
                }
                else if (weaponClass == 13) // bombos
                {
                    // bombos always breaks shell
                    table[weaponClass * 8 + 4] = 0x64;
                }
            }
            return table;
        }

    }
}
