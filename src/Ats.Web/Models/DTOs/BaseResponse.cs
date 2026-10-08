namespace Ats.Web.Models.DTOs;

public class BaseResponse<T>
{
    public bool IsSuccess { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }

    public static BaseResponse<T> SuccessResponse(T data, string? message = null)
    {
        return new BaseResponse<T> { IsSuccess = true, Data = data, Message = message };
    }

    public static BaseResponse<T> ErrorResponse(string message)
    {
        return new BaseResponse<T> { IsSuccess = false, Message = message };
    }
}
