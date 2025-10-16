using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Models;

namespace ProfetAPI.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        // --- DBSETS PARA USUARIOS Y CRM BASE ---
        public DbSet<Customer> Customers { get; set; } = null!;
        public DbSet<UserProfile> UserProfiles { get; set; } = null!;
        public DbSet<Team> Teams { get; set; } = null!;
        public DbSet<UserTeam> UserTeams { get; set; } = null!;

        // --- DBSETS PARA ACCOUNTS Y SU ECOSISTEMA ---
        public DbSet<Account> Accounts { get; set; } = null!;
        public DbSet<AccountInternalUser> AccountInternalUsers { get; set; } = null!;
        public DbSet<AccountNotificationRecipient> AccountNotificationRecipients { get; set; } = null!;
        public DbSet<AccountStatusHistory> AccountStatusHistories { get; set; } = null!;

        // --- DBSETS PARA FUNNELS Y PLANTILLAS ---
        public DbSet<Funnel> Funnels { get; set; } = null!;
        public DbSet<Stage> Stages { get; set; } = null!;
        public DbSet<FunnelTemplate> FunnelTemplates { get; set; } = null!;
        public DbSet<FunnelTemplateStage> FunnelTemplateStages { get; set; } = null!;

        // --- DBSETS PARA PLANES Y FACTURACIÓN ---
        public DbSet<Plan> Plans { get; set; } = null!;
        public DbSet<Feature> Features { get; set; } = null!;
        public DbSet<AddOn> AddOns { get; set; } = null!;
        public DbSet<PlanPriceHistory> PlanPriceHistories { get; set; } = null!;
        public DbSet<PlanFeature> PlanFeatures { get; set; } = null!;
        public DbSet<Subscription> Subscriptions { get; set; } = null!;
        public DbSet<SubscriptionPeriod> SubscriptionPeriods { get; set; } = null!;
        public DbSet<CustomerPurchasedAddOn> CustomerPurchasedAddOns { get; set; } = null!;
        
        // --- DBSETS PARA EL MOTOR DE CALIFICACIÓN ---
        public DbSet<ScoringModel> ScoringModels { get; set; } = null!;
        public DbSet<ScoringQuestion> ScoringQuestions { get; set; } = null!;
        public DbSet<ScoringAnswerOption> ScoringAnswerOptions { get; set; } = null!;
        public DbSet<ScoringRule> ScoringRules { get; set; } = null!;
        public DbSet<LeadTier> LeadTiers { get; set; } = null!;
        public DbSet<LeadScoringAnswer> LeadScoringAnswers { get; set; } = null!;
        public DbSet<TemplateCategory> TemplateCategories { get; set; } = null!;
        public DbSet<ScoringTemplate> ScoringTemplates { get; set; } = null!;
        public DbSet<ScoringTemplateQuestion> ScoringTemplateQuestions { get; set; } = null!;
        public DbSet<ScoringTemplateAnswerOption> ScoringTemplateAnswerOptions { get; set; } = null!;

        // --- DBSETS PARA CAMPOS PERSONALIZADOS ---
        public DbSet<CustomFieldDefinition> CustomFieldDefinitions { get; set; } = null!;
        public DbSet<AccountCustomField> AccountCustomFields { get; set; } = null!;
        public DbSet<CustomFieldValue> CustomFieldValues { get; set; } = null!;
        
        // --- DBSETS PARA EL NÚCLEO DEL CRM (LEADS/DEALS) ---
        public DbSet<Lead> Leads { get; set; } = null!;
        public DbSet<Company> Companies { get; set; } = null!;
        public DbSet<Contact> Contacts { get; set; } = null!;
        public DbSet<Deal> Deals { get; set; } = null!;
        public DbSet<DealUser> DealUsers { get; set; } = null!;

        // --- DBSETS PARA HISTORIAL POLIMÓRFICO Y SOPORTE ---
        public DbSet<Note> Notes { get; set; } = null!;
        public DbSet<Attachment> Attachments { get; set; } = null!;
        public DbSet<Activity> Activities { get; set; } = null!;
        public DbSet<CallDetail> CallDetails { get; set; } = null!;
        public DbSet<DealPayment> DealPayments { get; set; } = null!;
        public DbSet<ContactReferral> ContactReferrals { get; set; } = null!;
        public DbSet<Tag> Tags { get; set; } = null!;
        public DbSet<Tagging> Taggings { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<ItemCatalog> ItemCatalogs { get; set; } = null!;
        public DbSet<CatalogItem> CatalogItems { get; set; } = null!;
        public DbSet<DealItem> DealItems { get; set; } = null!;
        public DbSet<MessagesWhatsapp> MessagesWhatsapp { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<NotificationType> NotificationTypes { get; set; } = null!;
        public DbSet<Log> Logs { get; set; } = null!;
        public DbSet<ActivityPlaybook> ActivityPlaybooks { get; set; } = null!;
        public DbSet<PlaybookTask> PlaybookTasks { get; set; } = null!;
        public DbSet<Line> Lines { get; set; } = null!;
        public DbSet<UserLine> UserLines { get; set; } = null!;
        public DbSet<ProspectSource> ProspectSources { get; set; } = null!;
        public DbSet<Industry> Industries { get; set; } = null!;
        public DbSet<ContactForm> ContactForms { get; set; } = null!;
        
        // --- DBSETS PARA REPORTES ---
        public DbSet<Dashboard> Dashboards { get; set; } = null!;
        public DbSet<Widget> Widgets { get; set; } = null!;
        public DbSet<ChartType> ChartTypes { get; set; } = null!;
        public DbSet<ReportableField> ReportableFields { get; set; } = null!;
        public DbSet<ReportShareLink> ReportShareLinks { get; set; } = null!;
        public DbSet<SharedWidget> SharedWidgets { get; set; } = null!;


        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // --- MAPEAMOS LOS NOMBRES DE TABLA DE IDENTITY ---
            builder.Entity<ApplicationUser>().ToTable("Users");
            builder.Entity<ApplicationRole>().ToTable("Roles");
            builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
            // ... (resto de mapeos de Identity)

            // --- CONFIGURAMOS LAS RELACIONES Y LLAVES COMPUESTAS ---
            
            // Llaves Compuestas
            builder.Entity<UserTeam>().HasKey(ut => new { ut.UserId, ut.TeamId });
            builder.Entity<AccountInternalUser>().HasKey(aiu => new { aiu.AccountId, aiu.UserId, aiu.RoleInAccount });
            builder.Entity<PlanFeature>().HasKey(pf => new { pf.PlanId, pf.FeatureId });
            builder.Entity<AccountCustomField>().HasKey(acf => new { acf.AccountId, acf.FieldId });
            builder.Entity<DealUser>().HasKey(du => new { du.DealId, du.UserId });
            builder.Entity<DealItem>().HasKey(di => new { di.DealId, di.ItemId });
            builder.Entity<SharedWidget>().HasKey(sw => new { sw.ShareLinkId, sw.WidgetId });
            builder.Entity<Tagging>().HasKey(t => new { t.TagId, t.EntityId, t.EntityType });

            // Relaciones Específicas
            builder.Entity<ApplicationUser>(b => {
                b.HasOne(u => u.UserProfile).WithOne(p => p.User).HasForeignKey<UserProfile>(p => p.UserId);
                b.HasMany(u => u.Subordinates).WithOne(u => u.Parent).HasForeignKey(u => u.ParentId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ContactReferral>(b => {
                b.HasOne(cr => cr.ReferrerContact).WithMany(c => c.ReferralsMade).HasForeignKey(cr => cr.ReferrerContactId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(cr => cr.ReferredContact).WithMany(c => c.ReferralsReceived).HasForeignKey(cr => cr.ReferredContactId).OnDelete(DeleteBehavior.Restrict);
            });
            
            builder.Entity<Activity>(b => {
                b.HasOne(a => a.CallDetail).WithOne(cd => cd.Activity).HasForeignKey<CallDetail>(cd => cd.ActivityId);
            });

            // Mapeo de nombres de tabla para las que ya existen
            builder.Entity<Customer>().ToTable("Customers");
            builder.Entity<Team>().ToTable("Teams");
            builder.Entity<UserTeam>().ToTable("UserTeams");
            builder.Entity<Account>().ToTable("Accounts");
            builder.Entity<AccountStatusHistory>().ToTable("AccountStatusHistory");
            builder.Entity<Funnel>().ToTable("Funnels");
            builder.Entity<Stage>().ToTable("Stages");
            builder.Entity<CustomFieldDefinition>().ToTable("CustomFieldDefinitions");
            builder.Entity<Log>().ToTable("Logs");
            builder.Entity<Line>().ToTable("Lines");
            builder.Entity<UserLine>().ToTable("UserLines");
            builder.Entity<Industry>().ToTable("Industries");
            builder.Entity<ContactForm>().ToTable("ContactForms");
            builder.Entity<MessagesWhatsapp>().ToTable("MessagesWhatsapp");
            builder.Entity<Notification>().ToTable("Notifications");
            builder.Entity<NotificationType>().ToTable("NotificationTypes");

            // Nuevas tablas del núcleo del CRM
            builder.Entity<Lead>().ToTable("Leads");
            builder.Entity<Company>().ToTable("Companies");
            builder.Entity<Contact>().ToTable("Contacts");
            builder.Entity<Deal>().ToTable("Deals");
        }
    }
}