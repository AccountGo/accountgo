// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Dto.Common;
using System.Collections.Generic;
using Core.Domain.Recurring;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Services.Recurring
{
    /// <summary>
    /// Thrown when a schedule management action (create/update/pause/resume/end) is rejected for a business reason:
    /// a missing schedule, an archived customer on resume, an invalid cadence/line set, etc. The API maps this to a
    /// 400 with the safe message.
    /// </summary>
    public sealed class RecurringScheduleValidationException : Exception
    {
        public RecurringScheduleValidationException(string message) : base(message) { }
    }

    /// <summary>A line on a schedule, as edited in the UI.</summary>
    public sealed class RecurringScheduleLineInput
    {
        [PublicId("rschl")]
        public int? Id { get; set; }
        [PublicId("item")]
        public int? ItemId { get; set; }
        public string? Description { get; set; }
        public decimal Quantity { get; set; } = 1m;
        public decimal Amount { get; set; }
        public decimal Discount { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringDiscountType DiscountType { get; set; } = RecurringDiscountType.Percentage;
        [PublicId("tax")]
        public int? TaxId { get; set; }
        [PublicId("acct")]
        public int? AccountId { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>Create/update payload for a sales-invoice schedule.</summary>
    public sealed class RecurringScheduleInput
    {
        [PublicId("rsch")]
        public int Id { get; set; }
        [PublicId("cus")]
        public int CustomerId { get; set; }
        public string Name { get; set; } = "";
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringCadence Cadence { get; set; } = RecurringCadence.Monthly;
        public int Interval { get; set; } = 1;
        public int? DayOfMonth { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? MaxOccurrences { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringAction Action { get; set; } = RecurringAction.CreateDraft;
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringServicePeriodMode ServicePeriodMode { get; set; } = RecurringServicePeriodMode.CurrentMonth;
        [PublicId("term")]
        public int? PaymentTermId { get; set; }
        public string? Notes { get; set; }
        public string? PaymentInstructions { get; set; }
        public List<RecurringScheduleLineInput> Lines { get; set; } = new();
    }

    /// <summary>A line as returned to the UI.</summary>
    public sealed class RecurringScheduleLineView
    {
        [PublicId("rschl")]
        public int Id { get; set; }
        [PublicId("item")]
        public int? ItemId { get; set; }
        public string? ItemNo { get; set; }
        public string? Description { get; set; }
        public decimal Quantity { get; set; }
        public decimal Amount { get; set; }
        public decimal Discount { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringDiscountType DiscountType { get; set; }
        [PublicId("tax")]
        public int? TaxId { get; set; }
        [PublicId("acct")]
        public int? AccountId { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>A schedule summary row for the list page.</summary>
    public sealed class RecurringScheduleSummary
    {
        [PublicId("rsch")]
        public int Id { get; set; }
        public string Name { get; set; } = "";
        [PublicId("cus")]
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public bool CustomerArchived { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringScheduleStatus Status { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringCadence Cadence { get; set; }
        public int Interval { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringAction Action { get; set; }
        public DateTime? NextRunDate { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? MaxOccurrences { get; set; }
        public int OccurrenceCount { get; set; }
        public bool NeedsAttention { get; set; }
        public int ConsecutiveFailures { get; set; }
        /// <summary>True when NextRunDate is on or before the workspace's today and the schedule is Active.</summary>
        public bool IsOverdue { get; set; }
    }

    /// <summary>Full schedule detail for the detail/editor page.</summary>
    public sealed class RecurringScheduleDetail
    {
        [PublicId("rsch")]
        public int Id { get; set; }
        public string Name { get; set; } = "";
        [PublicId("cus")]
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public bool CustomerArchived { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringScheduleStatus Status { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringCadence Cadence { get; set; }
        public int Interval { get; set; }
        public int? DayOfMonth { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? MaxOccurrences { get; set; }
        public DateTime? NextRunDate { get; set; }
        public int OccurrenceCount { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringAction Action { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringServicePeriodMode ServicePeriodMode { get; set; }
        [PublicId("term")]
        public int? PaymentTermId { get; set; }
        public string? Notes { get; set; }
        public string? PaymentInstructions { get; set; }
        public bool NeedsAttention { get; set; }
        public int ConsecutiveFailures { get; set; }
        public bool IsOverdue { get; set; }
        public List<RecurringScheduleLineView> Lines { get; set; } = new();
        public IReadOnlyList<DateTime> UpcomingDates { get; set; } = Array.Empty<DateTime>();
    }

    /// <summary>One occurrence row in a schedule's history.</summary>
    public sealed class RecurringOccurrenceView
    {
        [PublicId("rocc")]
        public int Id { get; set; }
        [PublicId("rsch")]
        public int RecurringScheduleId { get; set; }
        public DateTime ScheduledRunDate { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public DateTime DocumentDate { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public RecurringOccurrenceStatus Status { get; set; }
        [PublicId("inv")]
        public int? SalesInvoiceId { get; set; }
        public string? ErrorCode { get; set; }
        public string? GuardFailureReason { get; set; }
        public string? SkipReason { get; set; }
        public string? ActionBy { get; set; }
        public int AttemptCount { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
