using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IFaComService
{
    Task<List<FaComAnnouncementDto>> GetFeedAsync();
    Task<List<FaComAnnouncementDto>> ListAnnouncementsAsync();
    Task<FaComAnnouncementDto> SaveAnnouncementAsync(int? id, FaComAnnouncementSaveDto dto);
    Task DeleteAnnouncementAsync(int id);
    Task<int> BroadcastAsync(int id, bool email, bool push, bool sms);

    Task<List<FaComTicketDto>> GetMyTicketsAsync();
    Task<FaComTicketDto> GetMyTicketAsync(int id);
    Task<FaComTicketDto> CreateMyTicketAsync(FaComTicketSaveDto dto);
    Task<FaComTicketDto> ReplyMyAsync(FaComReplySaveDto dto);

    Task<List<FaComTicketDto>> ListTicketsAsync(int? status = null, int? category = null);
    Task<FaComTicketDto> GetTicketAsync(int id);
    Task<FaComTicketDto> CreateTicketAsync(FaComTicketSaveDto dto);
    Task<FaComTicketDto> ReplyHrAsync(FaComReplySaveDto dto);
    Task<FaComTicketDto> SetStatusAsync(int id, int status);
    Task DeleteTicketAsync(int id);

    Task<int> CheckBirthdaysAsync();

    Task<List<FaComPollDto>> ListPollsAsync();
    Task<List<FaComPollDto>> ManagePollsAsync();
    Task<FaComPollDto> SavePollAsync(int? id, FaComPollSaveDto dto);
    Task DeletePollAsync(int id);
    Task<FaComPollDto> VoteAsync(int pollId, int optionId);
}

public class FaComService : IFaComService
{
    private readonly IApiClient _api;
    public FaComService(IApiClient api) => _api = api;

    private const string Root = "api/fa-com";

    public Task<List<FaComAnnouncementDto>> GetFeedAsync()
        => _api.GetAsync<List<FaComAnnouncementDto>>($"{Root}/announcements/feed");

    public Task<List<FaComAnnouncementDto>> ListAnnouncementsAsync()
        => _api.GetAsync<List<FaComAnnouncementDto>>($"{Root}/announcements");

    public Task<FaComAnnouncementDto> SaveAnnouncementAsync(int? id, FaComAnnouncementSaveDto dto)
        => id is > 0
            ? _api.PutAsync<FaComAnnouncementDto>($"{Root}/announcements/{id}", dto)
            : _api.PostAsync<FaComAnnouncementDto>($"{Root}/announcements", dto);

    public Task DeleteAnnouncementAsync(int id)
        => _api.DeleteAsync($"{Root}/announcements/{id}");

    public async Task<int> BroadcastAsync(int id, bool email, bool push, bool sms)
    {
        var r = await _api.PostAsync<Dictionary<string, int>>(
            $"{Root}/announcements/{id}/broadcast?email={email}&push={push}&sms={sms}", null);
        return r.TryGetValue("count", out var c) ? c : 0;
    }

    public Task<List<FaComTicketDto>> GetMyTicketsAsync()
        => _api.GetAsync<List<FaComTicketDto>>($"{Root}/tickets/my");

    public Task<FaComTicketDto> GetMyTicketAsync(int id)
        => _api.GetAsync<FaComTicketDto>($"{Root}/tickets/my/{id}");

    public Task<FaComTicketDto> CreateMyTicketAsync(FaComTicketSaveDto dto)
        => _api.PostAsync<FaComTicketDto>($"{Root}/tickets/my", dto);

    public Task<FaComTicketDto> ReplyMyAsync(FaComReplySaveDto dto)
        => _api.PostAsync<FaComTicketDto>($"{Root}/tickets/my/reply", dto);

    public Task<List<FaComTicketDto>> ListTicketsAsync(int? status = null, int? category = null)
    {
        var qs = new List<string>();
        if (status is >= 0) qs.Add($"status={status}");
        if (category is >= 0) qs.Add($"category={category}");
        var s = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<FaComTicketDto>>($"{Root}/tickets{s}");
    }

    public Task<FaComTicketDto> GetTicketAsync(int id)
        => _api.GetAsync<FaComTicketDto>($"{Root}/tickets/{id}");

    public Task<FaComTicketDto> CreateTicketAsync(FaComTicketSaveDto dto)
        => _api.PostAsync<FaComTicketDto>($"{Root}/tickets", dto);

    public Task<FaComTicketDto> ReplyHrAsync(FaComReplySaveDto dto)
        => _api.PostAsync<FaComTicketDto>($"{Root}/tickets/reply", dto);

    public Task<FaComTicketDto> SetStatusAsync(int id, int status)
        => _api.PostAsync<FaComTicketDto>($"{Root}/tickets/{id}/status?status={status}", null);

    public Task DeleteTicketAsync(int id)
        => _api.DeleteAsync($"{Root}/tickets/{id}");

    public async Task<int> CheckBirthdaysAsync()
    {
        var r = await _api.PostAsync<Dictionary<string, int>>($"{Root}/birthdays/check", null);
        return r.TryGetValue("count", out var c) ? c : 0;
    }

    public Task<List<FaComPollDto>> ListPollsAsync()
        => _api.GetAsync<List<FaComPollDto>>($"{Root}/polls");

    public Task<List<FaComPollDto>> ManagePollsAsync()
        => _api.GetAsync<List<FaComPollDto>>($"{Root}/polls/manage");

    public Task<FaComPollDto> SavePollAsync(int? id, FaComPollSaveDto dto)
        => id is > 0
            ? _api.PutAsync<FaComPollDto>($"{Root}/polls/{id}", dto)
            : _api.PostAsync<FaComPollDto>($"{Root}/polls", dto);

    public Task DeletePollAsync(int id)
        => _api.DeleteAsync($"{Root}/polls/{id}");

    public Task<FaComPollDto> VoteAsync(int pollId, int optionId)
        => _api.PostAsync<FaComPollDto>($"{Root}/polls/{pollId}/vote?optionId={optionId}", null);
}
