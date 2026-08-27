using PrivacyEventProcessing.Domain.Models;

namespace PrivacyEventProcessing.Domain.Interfaces
{
    public interface IPrivacyService
    {
        string HashUserId(string userId);
        string MaskEmail(string email);
        string MaskIpAddress(string ipAddress);
        bool ValidateInput(EventRequest request, out string errorMessage);
    }
}
