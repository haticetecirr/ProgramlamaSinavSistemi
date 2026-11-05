namespace ProgLab.Server.Data.Entities;

/// <summary>Bir sınavın sorusu, sırası ve sınava özel puanı. Cevaplar bu satırın kimliğine bağlanır.</summary>
public class ExamQuestion
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public int QuestionId { get; set; }
    public short OrderNo { get; set; }
    public decimal Points { get; set; }

    public Exam? Exam { get; set; }
    public Question? Question { get; set; }
}
