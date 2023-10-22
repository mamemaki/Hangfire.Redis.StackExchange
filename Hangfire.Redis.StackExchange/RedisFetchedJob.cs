// Copyright © 2013-2015 Sergey Odinokov, Marco Casamento
// This software is based on https://github.com/HangfireIO/Hangfire.Redis

// Hangfire.Redis.StackExchange is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as
// published by the Free Software Foundation, either version 3
// of the License, or any later version.
//
// Hangfire.Redis.StackExchange is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with Hangfire.Redis.StackExchange. If not, see <http://www.gnu.org/licenses/>.

using System;
using Hangfire.Annotations;
using Hangfire.Storage;
using StackExchange.Redis;

namespace Hangfire.Redis.StackExchange
{
    internal class RedisFetchedJob : IFetchedJob
    {
        private readonly RedisStorage _storage;
		    private readonly IDatabase _redis;
        private bool _disposed;
        private bool _removedFromQueue;
        private bool _requeued;

        public RedisFetchedJob(
            [NotNull] RedisStorage storage, 
            [NotNull] IDatabase redis,
            [NotNull] string jobId, 
            [NotNull] string queue)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            if (redis == null) throw new ArgumentNullException(nameof(redis));
            if (jobId == null) throw new ArgumentNullException(nameof(jobId));
            if (queue == null) throw new ArgumentNullException(nameof(queue));

            _storage = storage;
            _redis = redis;

            JobId = jobId;
            Queue = queue;

            _storage.ResourceBudgetManager?.AddFetchedJob(this);
        }

        public string JobId { get; }
        public string Queue { get; }

        public void RemoveFromQueue()
        {
            if (_storage.UseTransactions)
            {
                var transaction = _redis.CreateTransaction();
                RemoveFromFetchedList(transaction);
                transaction.Execute();                
            } else
            {
                RemoveFromFetchedList(_redis);
            }
            _removedFromQueue = true;
        }

        public void Requeue()
        {
            Requeue(_storage, _redis, JobId, Queue);
            _requeued = true;
        }

        public static void Requeue([NotNull] RedisStorage storage, [NotNull] IDatabase redis,
            [NotNull] string jobId, [NotNull] string queue)
        {
            if (storage.UseTransactions)
            {
                var transaction = redis.CreateTransaction();
                transaction.ListRightPushAsync(storage.GetRedisKey($"queue:{queue}"), jobId);
                RemoveFromFetchedList(transaction, storage, jobId, queue);
                transaction.Execute();
            } else
            {
                redis.ListRightPushAsync(storage.GetRedisKey($"queue:{queue}"), jobId);
                RemoveFromFetchedList(redis, storage, jobId, queue);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (!_removedFromQueue && !_requeued)
            {
                Requeue();
            }

            _disposed = true;
        }

        private void RemoveFromFetchedList(IDatabaseAsync databaseAsync)
        {
            RemoveFromFetchedList(databaseAsync, _storage, JobId, Queue);
            _storage.ResourceBudgetManager?.RemoveFetchedJob(this);
        }

        private static void RemoveFromFetchedList(IDatabaseAsync databaseAsync, RedisStorage storage,
            string jobId, string queue)
        {
            databaseAsync.ListRemoveAsync(storage.GetRedisKey($"queue:{queue}:dequeued"), jobId, -1);
            databaseAsync.HashDeleteAsync(storage.GetRedisKey($"job:{jobId}"), new RedisValue[] { "Fetched", "Checked" });
        }
    }
}
