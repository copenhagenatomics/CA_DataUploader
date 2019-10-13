using CA_DataUploaderLib;
using System.Drawing;


namespace CA_AverageTemperature
{
    public class JackRectangle
    {
        public SensorSample Sensor;
        public Rectangle Rect;
        public Point TextPos;
        public Point TitlePos;
        public Point SerialPos;

        public JackRectangle(SensorSample sensor, double width, double height)
        {
            Sensor = sensor;
            int x = (int)(((16- sensor.Jack) % 8 + 0.5) * width);
            int y = (int)(((sensor.Hub) * 4 + ((sensor.Jack-1) / 8) + 1) * height);
            Rect = new Rectangle(x, y, (int)width, (int)height);
            if(sensor.Jack == 0)
            {
                Rect = new Rectangle((int)(9 * width), (int)(y + height / 2), (int)width, (int)height);
                TitlePos = Rect.Location;
                TitlePos.Y -= Rect.Height / 2;
                TitlePos.X += Rect.Width / 2;
                SerialPos = Rect.Location;
                SerialPos.Y += (int)(Rect.Height * 1.5);
                SerialPos.X += Rect.Width / 2;
            }

            TextPos = Rect.Location;
            TextPos.X += Rect.Width / 2;
            TextPos.Y += Rect.Height / 2;
        }

        public string Text { get { return Sensor.Value < 1000?Sensor.Value.ToString("N1"): Sensor.Value.ToString("N0");  } }

        public override string ToString()
        {
            return $"hub:{Sensor.Hub}, jack:{Sensor.Jack} x:{Rect.X}, y:{Rect.Y}";
        }
    }
}
