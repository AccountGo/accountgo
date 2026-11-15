// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Core.Domain.Recurring;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Services.Recurring;
using Api.Controllers;

namespace AccountGo.ServicesTests.Recurring;

public class RecurringJsonContractTests
{
    [Theory]
    [InlineData("\"Monthly\"", "\"AutoIssue\"", "\"PreviousMonth\"", "\"Amount\"")]
    [InlineData("2", "2", "2", "1")]
    public void Input_accepts_names_and_legacy_numbers(string cadence, string action, string period, string discount)
    {
        var input = JsonConvert.DeserializeObject<RecurringScheduleInput>(
            $$"""{"Cadence":{{cadence}},"Action":{{action}},"ServicePeriodMode":{{period}},"Lines":[{"DiscountType":{{discount}}}]}""")!;
        Assert.Equal(RecurringCadence.Monthly, input.Cadence);
        Assert.Equal(RecurringAction.AutoIssue, input.Action);
        Assert.Equal(RecurringServicePeriodMode.PreviousMonth, input.ServicePeriodMode);
        Assert.Equal(RecurringDiscountType.Amount, input.Lines[0].DiscountType);
        var json = JObject.FromObject(input);
        Assert.Equal("Monthly", json["Cadence"]!.Value<string>());
        Assert.Equal("AutoIssue", json["Action"]!.Value<string>());
        Assert.Equal("PreviousMonth", json["ServicePeriodMode"]!.Value<string>());
        Assert.Equal("Amount", json["Lines"]![0]!["DiscountType"]!.Value<string>());
        var preview = JsonConvert.DeserializeObject<RecurringSchedulesController.PreviewRequest>(
            $$"""{"Cadence":{{cadence}}}""")!;
        Assert.Equal(RecurringCadence.Monthly, preview.Cadence);
        Assert.Equal("Monthly", JObject.FromObject(preview)["Cadence"]!.Value<string>());
    }

    [Fact]
    public void Responses_serialize_recurring_enums_as_names()
    {
        var summary = JObject.FromObject(new RecurringScheduleSummary { Status = RecurringScheduleStatus.Active, Cadence = RecurringCadence.Monthly, Action = RecurringAction.AutoIssue });
        Assert.Equal("Active", summary["Status"]!.Value<string>());
        Assert.Equal("Monthly", summary["Cadence"]!.Value<string>());
        Assert.Equal("AutoIssue", summary["Action"]!.Value<string>());
        var detail = JObject.FromObject(new RecurringScheduleDetail { Status = RecurringScheduleStatus.Paused, Cadence = RecurringCadence.Weekly, Action = RecurringAction.CreateDraft, ServicePeriodMode = RecurringServicePeriodMode.PreviousMonth, Lines = new() { new() { DiscountType = RecurringDiscountType.Amount } } });
        Assert.Equal("Paused", detail["Status"]!.Value<string>());
        Assert.Equal("Weekly", detail["Cadence"]!.Value<string>());
        Assert.Equal("CreateDraft", detail["Action"]!.Value<string>());
        Assert.Equal("PreviousMonth", detail["ServicePeriodMode"]!.Value<string>());
        Assert.Equal("Amount", detail["Lines"]![0]!["DiscountType"]!.Value<string>());
        Assert.Equal("DraftCreated", JObject.FromObject(new RecurringOccurrenceView { Status = RecurringOccurrenceStatus.DraftCreated })["Status"]!.Value<string>());
    }
}
