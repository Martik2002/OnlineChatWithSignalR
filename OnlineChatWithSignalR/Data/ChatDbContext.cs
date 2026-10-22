using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace OnlineChatWithSignalR.Data;

public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options)
    : IdentityDbContext<ChatUser>(options);
