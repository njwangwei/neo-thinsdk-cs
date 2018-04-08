﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace smartContractDemo
{
    //发布智能合约的例子
    class Demo4
    {
        string uri = "https://api.nel.group";
        string url = "/api/testnet";
        string api = "https://api.nel.group/api/testnet";

        httpHelper http = new httpHelper();

        string id_GAS = "0x602c79718b16e442de58778e148d0b1084e3b2dffd5de6b7b16cee7969282de7";
        public void Demo()
        {
            Console.WriteLine("请输入你的wif");
            string  wif = Console.ReadLine();
            if (string.IsNullOrEmpty(wif))
            {
                wif = "";  //这里填你用于支付发布合约消耗的私钥
            }
            byte[] prikey = ThinNeo.Helper.GetPrivateKeyFromWIF(wif);
            byte[] pubkey = ThinNeo.Helper.GetPublicKeyFromPrivateKey(prikey);
            string address = ThinNeo.Helper.GetAddressFromPublicKey(pubkey);

            Dictionary<string, List<Utxo>> dir = GetBalanceByAddress(address);

            //从文件中读取合约脚本
            //byte[] script = System.IO.File.ReadAllBytes("Nep5.avm"); //这里填你的合约所在地址
            byte[] script = System.IO.File.ReadAllBytes("{合约avm路径}"); //这里填你的合约所在地址
            //Console.WriteLine("合约脚本:"+ThinNeo.Helper.Bytes2HexString(script));
            //Console.WriteLine("合约脚本hash："+ThinNeo.Helper.Bytes2HexString(ThinNeo.Helper.GetScriptHashFromScript(script).Reverse().ToArray()));
            //byte[] parameter__list = ThinNeo.Helper.HexString2Bytes("0610");  //这里填合约入参  例：0610代表（string，[]）
            byte[] parameter__list = ThinNeo.Helper.HexString2Bytes("{合约入参}");  //这里填合约入参  例：0610代表（string，[]）
            //byte[] return_type = ThinNeo.Helper.HexString2Bytes("05");  //这里填合约的出参
            byte[] return_type = ThinNeo.Helper.HexString2Bytes("{合约出参}");  //这里填合约的出参
            int need_storage = 1;   
            int need_nep4 = 0;
            string name = "viko";
            string version = "0";
            string auther = "viko";
            string email = "82604458@qq.com";
            string description = "*****";
            using (ThinNeo.ScriptBuilder sb = new ThinNeo.ScriptBuilder())
            {
                //倒叙插入数据
                sb.EmitPushString(description);
                sb.EmitPushString(email);
                sb.EmitPushString(auther);
                sb.EmitPushString(version);
                sb.EmitPushString(name);
                sb.EmitPushNumber(need_storage|need_nep4);
                sb.EmitPushBytes(return_type);
                sb.EmitPushBytes(parameter__list);
                sb.EmitPushBytes(script);
                sb.EmitSysCall("Neo.Contract.Create");

                string scriptPublish = ThinNeo.Helper.Bytes2HexString(sb.ToArray());
                //用ivokescript试运行并得到消耗
                string result = http.Post(api, "invokescript", new MyJson.JsonNode_Array() { new MyJson.JsonNode_ValueString(scriptPublish) },Encoding.UTF8);
                var consume =((( MyJson.Parse(result) as MyJson.JsonNode_Object)["result"] as MyJson.JsonNode_Array)[0] as MyJson.JsonNode_Object)["gas_consumed"].ToString();
                decimal gas_consumed = decimal.Parse(consume);
                ThinNeo.InvokeTransData extdata = new ThinNeo.InvokeTransData();
                extdata.script = sb.ToArray();

                //Console.WriteLine(ThinNeo.Helper.Bytes2HexString(extdata.script));
                extdata.gas = Math.Ceiling(gas_consumed-10);

                //拼装交易体
                ThinNeo.Transaction tran = makeTran(dir,null, id_GAS, extdata.gas);
                tran.version = 1;
                tran.extdata = extdata;
                tran.type = ThinNeo.TransactionType.InvocationTransaction;
                byte[] msg = tran.GetMessage();
                byte[] signdata = ThinNeo.Helper.Sign(msg, prikey);
                tran.AddWitness(signdata, pubkey, address);
                string txid = ThinNeo.Helper.Bytes2HexString(tran.GetHash().Reverse().ToArray());
                byte[] data = tran.GetRawData();
                string scripthash = ThinNeo.Helper.Bytes2HexString(data);

                //Console.WriteLine("scripthash:"+scripthash);
                result = http.Post(api, "sendrawtransaction", new MyJson.JsonNode_Array() { new MyJson.JsonNode_ValueString(scripthash) }, Encoding.UTF8);
                MyJson.JsonNode_Object resJO = (MyJson.JsonNode_Object)MyJson.Parse(result);
                Console.WriteLine(resJO.ToString());
            }
        }

        //获取地址的utxo来得出地址的资产  
        Dictionary<string, List<Utxo>> GetBalanceByAddress(string _addr)
        {
            MyJson.JsonNode_Object response = (MyJson.JsonNode_Object)MyJson.Parse(http.HttpGet(api + "?method=getutxo&id=1&params=['" + _addr + "']"));
            MyJson.JsonNode_Array resJA = (MyJson.JsonNode_Array)response["result"];
            Dictionary<string, List<Utxo>> _dir = new Dictionary<string, List<Utxo>>();
            foreach (MyJson.JsonNode_Object j in resJA)
            {
                Utxo utxo = new Utxo(j["addr"].ToString(), j["txid"].ToString(), j["asset"].ToString(), decimal.Parse(j["value"].ToString()), int.Parse(j["n"].ToString()));
                if (_dir.ContainsKey(j["asset"].ToString()))
                {
                    _dir[j["asset"].ToString()].Add(utxo);
                }
                else
                {
                    List<Utxo> l = new List<Utxo>();
                    l.Add(utxo);
                    _dir[j["asset"].ToString()] = l;
                }

            }
            return _dir;
        }

        //拼交易体
        ThinNeo.Transaction makeTran(Dictionary<string, List<Utxo>> dir_utxos, string targetaddr, string assetid, decimal sendcount)
        {
            if (!dir_utxos.ContainsKey(assetid))
                throw new Exception("no enough money.");

            List<Utxo> utxos = dir_utxos[assetid];
            var tran = new ThinNeo.Transaction();
            tran.type = ThinNeo.TransactionType.ContractTransaction;
            tran.version = 0;//0 or 1
            tran.extdata = null;

            tran.attributes = new ThinNeo.Attribute[0];
            var scraddr = "";
            utxos.Sort((a, b) =>
            {
                if (a.value > b.value)
                    return 1;
                else if (a.value < b.value)
                    return -1;
                else
                    return 0;
            });
            decimal count = decimal.Zero;
            List<ThinNeo.TransactionInput> list_inputs = new List<ThinNeo.TransactionInput>();
            for (var i = 0; i < utxos.Count; i++)
            {
                ThinNeo.TransactionInput input = new ThinNeo.TransactionInput();
                input.hash = ThinNeo.Helper.HexString2Bytes(utxos[i].txid.Replace("0x", "")).Reverse().ToArray();
                input.index = (ushort)utxos[i].n;
                list_inputs.Add(input);
                count += utxos[i].value;
                scraddr = utxos[i].addr;
                if (count >= sendcount)
                {
                    break;
                }
            }
            tran.inputs = list_inputs.ToArray();
            if (count >= sendcount)//输入大于等于输出
            {
                List<ThinNeo.TransactionOutput> list_outputs = new List<ThinNeo.TransactionOutput>();
                //输出
                if (sendcount > decimal.Zero && targetaddr != null)
                {
                    ThinNeo.TransactionOutput output = new ThinNeo.TransactionOutput();
                    output.assetId = ThinNeo.Helper.HexString2Bytes(assetid.Replace("0x", "")).Reverse().ToArray();
                    output.value = sendcount;
                    output.toAddress = ThinNeo.Helper.GetPublicKeyHashFromAddress(targetaddr);
                    list_outputs.Add(output);
                }

                //找零
                var change = count - sendcount;
                if (change > decimal.Zero)
                {
                    ThinNeo.TransactionOutput outputchange = new ThinNeo.TransactionOutput();
                    outputchange.toAddress = ThinNeo.Helper.GetPublicKeyHashFromAddress(scraddr);
                    outputchange.value = change;
                    outputchange.assetId = ThinNeo.Helper.HexString2Bytes(assetid.Replace("0x", "")).Reverse().ToArray();
                    list_outputs.Add(outputchange);

                }
                tran.outputs = list_outputs.ToArray();
            }
            else
            {
                throw new Exception("no enough money.");
            }
            return tran;
        }

        public string MakeRpcUrl(string url, string method, params MyJson.IJsonNode[] _params)
        {
            StringBuilder sb = new StringBuilder();
            if (url.Last() != '/')
                url = url + "/";

            sb.Append(url + "?jsonrpc=2.0&id=1&method=" + method + "&params=[");
            for (var i = 0; i < _params.Length; i++)
            {
                _params[i].ConvertToString(sb);
                if (i != _params.Length - 1)
                    sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }
    }
}
