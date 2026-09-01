// DarknetEconomy.cs — The Daemon's reputation-backed credit system
// Darknet credits are the economy of D-Space. They're earned through quests,
// contributions, and mesh network participation. Spent on D-Space items,
// services, and level-gated capabilities. The economy is distributed —
// no central bank, transactions verified by the mesh.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Identity;

namespace DaemonVision.Economy
{
    public class DarknetEconomy : SubsystemBase
    {
        public override string Name => "Economy";

        [Header("Economy Settings")]
        [SerializeField] private long startingCredits = 100;
        [SerializeField] private float transactionFeePercent = 0.5f; // 0.5% mesh network fee
        [SerializeField] private int maxTransactionHistorySize = 500;

        private DarknetIdentityManager identityManager;

        private long balance;
        private readonly List<Transaction> transactionHistory = new List<Transaction>();
        private readonly Queue<Transaction> pendingTransactions = new Queue<Transaction>();

        public event Action<long> OnBalanceChanged;
        public event Action<Transaction> OnTransactionCompleted;

        protected override Task OnInitialize()
        {
            balance = PlayerPrefs.GetInt("darknet_credits", (int)startingCredits);
            LoadTransactionHistory();
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
        }

        public long GetBalance() => balance;

        /// <summary>
        /// Earn credits — from quest rewards, mesh hosting, contributions, etc.
        /// </summary>
        public void EarnCredits(long amount, string source)
        {
            if (amount <= 0) return;

            balance += amount;
            var tx = RecordTransaction(TransactionType.Earn, amount, source);

            Log($"+{amount} credits from {source}. Balance: {balance}");
            OnBalanceChanged?.Invoke(balance);
            OnTransactionCompleted?.Invoke(tx);
            SaveBalance();
        }

        /// <summary>
        /// Spend credits on D-Space items, services, or transfers.
        /// </summary>
        public SpendResult SpendCredits(long amount, string purpose)
        {
            if (amount <= 0) return SpendResult.InvalidAmount;
            if (amount > balance) return SpendResult.InsufficientFunds;

            balance -= amount;
            var tx = RecordTransaction(TransactionType.Spend, amount, purpose);

            Log($"-{amount} credits for {purpose}. Balance: {balance}");
            OnBalanceChanged?.Invoke(balance);
            OnTransactionCompleted?.Invoke(tx);
            SaveBalance();
            return SpendResult.Success;
        }

        /// <summary>
        /// Transfer credits to another operative via the mesh network.
        /// Includes a small network fee.
        /// </summary>
        public TransferResult TransferCredits(string targetAddress, long amount, string memo)
        {
            if (amount <= 0) return TransferResult.InvalidAmount;

            long fee = (long)Math.Ceiling(amount * transactionFeePercent / 100.0);
            long totalCost = amount + fee;

            if (totalCost > balance) return TransferResult.InsufficientFunds;

            if (identityManager?.GetIdentity(targetAddress) == null)
                return TransferResult.RecipientNotFound;

            balance -= totalCost;

            var tx = RecordTransaction(TransactionType.Transfer, amount,
                $"Transfer to {AddressUtil.Short(targetAddress)}: {memo}");
            tx.TargetAddress = targetAddress;
            tx.Fee = fee;

            pendingTransactions.Enqueue(tx);

            Log($"Transferred {amount} credits to {AddressUtil.Short(targetAddress)} (fee: {fee}). Balance: {balance}");
            OnBalanceChanged?.Invoke(balance);
            OnTransactionCompleted?.Invoke(tx);
            SaveBalance();
            return TransferResult.Success;
        }

        /// <summary>
        /// Receive a credit transfer from the mesh network.
        /// </summary>
        public void ReceiveTransfer(string fromAddress, long amount, string memo)
        {
            balance += amount;
            var tx = RecordTransaction(TransactionType.Receive, amount,
                $"Received from {AddressUtil.Short(fromAddress)}: {memo}");
            tx.TargetAddress = fromAddress;

            Log($"Received {amount} credits from {AddressUtil.Short(fromAddress)} Balance: {balance}");
            OnBalanceChanged?.Invoke(balance);
            OnTransactionCompleted?.Invoke(tx);
            SaveBalance();
        }

        public Transaction DequeuePendingTransaction()
        {
            return pendingTransactions.Count > 0 ? pendingTransactions.Dequeue() : null;
        }

        public IReadOnlyList<Transaction> GetTransactionHistory() => transactionHistory;

        public bool HasPendingTransactions => pendingTransactions.Count > 0;

        private Transaction RecordTransaction(TransactionType type, long amount, string description)
        {
            var tx = new Transaction
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Type = type,
                Amount = amount,
                Description = description,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                BalanceAfter = balance
            };

            transactionHistory.Insert(0, tx);
            if (transactionHistory.Count > maxTransactionHistorySize)
                transactionHistory.RemoveAt(transactionHistory.Count - 1);

            SaveTransactionHistory();
            return tx;
        }

        private void SaveBalance()
        {
            PlayerPrefs.SetInt("darknet_credits", (int)balance);
        }

        private void LoadTransactionHistory()
        {
            string json = PlayerPrefs.GetString("darknet_transactions", "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<TransactionHistoryWrapper>(json);
                    if (wrapper?.Transactions != null)
                        transactionHistory.AddRange(wrapper.Transactions);
                }
                catch { }
            }
        }

        private void SaveTransactionHistory()
        {
            var wrapper = new TransactionHistoryWrapper { Transactions = transactionHistory };
            PlayerPrefs.SetString("darknet_transactions", JsonUtility.ToJson(wrapper));
        }

        protected override void OnShutdown()
        {
            SaveBalance();
            SaveTransactionHistory();
        }
    }

    [Serializable]
    public class Transaction
    {
        public string Id;
        public TransactionType Type;
        public long Amount;
        public long Fee;
        public string Description;
        public string TargetAddress;
        public long Timestamp;
        public long BalanceAfter;
    }

    [Serializable]
    public class TransactionHistoryWrapper
    {
        public List<Transaction> Transactions;
    }

    public enum TransactionType { Earn, Spend, Transfer, Receive }
    public enum SpendResult { Success, InsufficientFunds, InvalidAmount }
    public enum TransferResult { Success, InsufficientFunds, InvalidAmount, RecipientNotFound, NetworkError }
}
