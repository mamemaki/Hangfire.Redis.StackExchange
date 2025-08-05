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
using Hangfire.Common;
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
            [NotNull] string queue,
            [CanBeNull] DateTime? fetchedAt)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
            Queue = queue ?? throw new ArgumentNullException(nameof(queue));
            FetchedAt = fetchedAt;

            _storage.ResourceBudgetManager?.AddFetchedJob(this);
        }

        public string JobId { get; }
        public string Queue { get; }
        public DateTime? FetchedAt { get; }

        private DateTime? GetFetchedValue()
        {
            return JobHelper.DeserializeNullableDateTime(_redis.HashGet(_storage.GetRedisKey($"job:{JobId}"), "Fetched"));
        }
        
        public void RemoveFromQueue()
        {
            var fetchedAt = GetFetchedValue();
            if (_storage.UseTransactions)
            {
                var transaction = _redis.CreateTransaction();

                if (fetchedAt == FetchedAt)
                {
                    RemoveFromFetchedListAsync(transaction);
                }
                transaction.PublishAsync(_storage.SubscriptionChannel, JobId);
                transaction.Execute();                
            } 
            else
            {
                if (fetchedAt == FetchedAt)
                {
                    RemoveFromFetchedList(_redis);
                }

                _redis.Publish(_storage.SubscriptionChannel, JobId);
            }
            _removedFromQueue = true;
        }

        public void Requeue()
        {
            var fetchedAt = GetFetchedValue();
            Requeue(_storage, _redis, JobId, Queue, (fetchedAt == FetchedAt));
            _requeued = true;
        }

        public static void Requeue([NotNull] RedisStorage storage, [NotNull] IDatabase redis,
            [NotNull] string jobId, [NotNull] string queue, bool isFetchedAtSame)
        {
            if (storage.UseTransactions)
            {
                var transaction = redis.CreateTransaction();
                transaction.ListRightPushAsync(storage.GetRedisKey($"queue:{queue}"), jobId);
                if (isFetchedAtSame)
                {
                    RemoveFromFetchedListAsync(transaction, storage, jobId, queue);
                }

                transaction.PublishAsync(storage.SubscriptionChannel, jobId);
                transaction.Execute();
            }
            else
            {
                redis.ListRightPush(storage.GetRedisKey($"queue:{queue}"), jobId);
                if (isFetchedAtSame)
                {
                    RemoveFromFetchedList(redis, storage, jobId, queue);
                }

                redis.Publish(storage.SubscriptionChannel, jobId);
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

        private void RemoveFromFetchedListAsync(IDatabaseAsync databaseAsync)
        {
            RemoveFromFetchedListAsync(databaseAsync, _storage, JobId, Queue);
            _storage.ResourceBudgetManager?.RemoveFetchedJob(this);
        }

        private static void RemoveFromFetchedListAsync(IDatabaseAsync databaseAsync,
            RedisStorage storage, string jobId, string queue)
        {
            databaseAsync.ListRemoveAsync(storage.GetRedisKey($"queue:{queue}:dequeued"), jobId, -1);
            databaseAsync.HashDeleteAsync(storage.GetRedisKey($"job:{jobId}"), ["Fetched", "Checked"]);
        }

        private void RemoveFromFetchedList(IDatabase database)
        {
            RemoveFromFetchedList(database, _storage, JobId, Queue);

            _storage.ResourceBudgetManager?.RemoveFetchedJob(this);
        }

        private static void RemoveFromFetchedList(IDatabase database,
            RedisStorage storage, string jobId, string queue)
        {
            database.ListRemove(storage.GetRedisKey($"queue:{queue}:dequeued"), jobId, -1);
            database.HashDelete(storage.GetRedisKey($"job:{jobId}"), ["Fetched", "Checked"]);
        }
    }
}
