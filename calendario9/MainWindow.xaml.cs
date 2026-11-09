using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Login
{
    /// <summary>Colores de la aplicación y funciones para saber si un día es fin de semana.</summary>
    internal static class Paleta
    {
        private static SolidColorBrush B(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        public static readonly Brush Libre = B("#22A060");
        public static readonly Brush Ocupado = B("#E14040");
        public static readonly Brush Festivo = B("#EAB308");
        public static readonly Brush Finde = B("#5E617A");
        public static readonly Brush TextoFinde = B("#D3D5E4");
        public static readonly Brush Acento = B("#6366F1");
        public static readonly Brush TextoSuave = B("#8C8FA5");
        public static readonly Brush TextoSobreAmarillo = B("#282000");
        public static readonly Brush Hover = B("#99FFFFFF");
        public static readonly Brush TextoDialogo = B("#6B6E85");
        public static readonly Brush Error = B("#C62828");

        /// <summary>true = sábados y domingos no se pueden bloquear (quedan en gris).</summary>
        public const bool FindesNoBloqueables = true;

        public static bool EsFinde(DateTime d)
        {
            return d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
        }

        /// <summary>Indica si el día no se puede pulsar ni bloquear (los fines de semana).</summary>
        public static bool NoBloqueable(DateTime d)
        {
            return FindesNoBloqueables && EsFinde(d);
        }

        public static bool EsFestivo(string tipo)
        {
            return string.Equals(tipo, BloqueosCalendarioService.TipoFestivo, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Devuelve el color de un bloqueo según su tipo (rojo ocupado, amarillo festivo).</summary>
        public static Brush Tipo(string tipo)
        {
            return EsFestivo(tipo) ? Festivo : Ocupado;
        }
    }

    /// <summary>Opción del desplegable "Motivo".</summary>
    public class TipoItem
    {
        public string Nombre { get; set; }
        public string Valor { get; set; }    // valor que se guarda en el servicio
        public Brush Color { get; set; }
    }

    /// <summary>
    /// Un bloqueo del calendario: un rango de fechas [FechaInicio, FechaFin] (ambas incluidas).
    /// Es el equivalente a la entidad BloqueosCalendario del proyecto Spring Boot
    /// (fecha_inicio, fecha_fin, tipo). Un único día es un rango con inicio == fin.
    /// </summary>
    public class Bloqueo
    {
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public string Tipo { get; set; }

        public Bloqueo(DateTime inicio, DateTime fin, string tipo)
        {
            FechaInicio = inicio.Date;
            FechaFin = fin.Date;
            Tipo = tipo;
        }
    }

    /// <summary>
    /// Misma lógica que BloqueosCalendarioService.java, pero en memoria:
    /// no se conecta a ningún servidor ni base de datos.
    /// </summary>
    public class BloqueosCalendarioService
    {
        public const string TipoOcupado = "OCUPADO";
        public const string TipoFestivo = "FESTIVO";

        private readonly List<Bloqueo> _bloqueos = new List<Bloqueo>();

        /// <summary>
        /// Bloquea un rango de fechas (ambas incluidas). Si pisa bloqueos anteriores,
        /// esa parte se sustituye por el nuevo tipo. Los bloqueos contiguos del
        /// mismo tipo se unen en uno solo.
        /// </summary>
        public void BloquearRango(DateTime inicio, DateTime fin, string tipo)
        {
            ValidarRango(inicio, fin);
            DesbloquearRango(inicio, fin);                       // evita solapes
            _bloqueos.Add(new Bloqueo(inicio, fin, tipo));
            Fusionar();
        }

        /// <summary>Bloquea un único día: es un rango donde inicio == fin.</summary>
        public void BloquearDia(DateTime dia, string tipo)
        {
            BloquearRango(dia, dia, tipo);
        }

        /// <summary>Desbloquea un único día.</summary>
        public void DesbloquearDia(DateTime dia)
        {
            DesbloquearRango(dia, dia);
        }

        /// <summary>
        /// Desbloquea un rango [desde, hasta]. Cada bloqueo existente que se solape se
        /// recorta o se parte en dos, o se elimina si queda completamente dentro del rango.
        /// </summary>
        public void DesbloquearRango(DateTime desde, DateTime hasta)
        {
            ValidarRango(desde, hasta);
            desde = desde.Date;
            hasta = hasta.Date;

            List<Bloqueo> afectados = _bloqueos
                .Where(b => b.FechaInicio <= hasta && b.FechaFin >= desde)
                .ToList();

            foreach (Bloqueo b in afectados)
            {
                bool sobraIzquierda = b.FechaInicio < desde;
                bool sobraDerecha = b.FechaFin > hasta;

                if (sobraIzquierda && sobraDerecha)
                {
                    // El hueco está en medio -> partimos el bloqueo en dos
                    DateTime finOriginal = b.FechaFin;
                    b.FechaFin = desde.AddDays(-1);
                    _bloqueos.Add(new Bloqueo(hasta.AddDays(1), finOriginal, b.Tipo));
                }
                else if (sobraIzquierda)
                {
                    b.FechaFin = desde.AddDays(-1);
                }
                else if (sobraDerecha)
                {
                    b.FechaInicio = hasta.AddDays(1);
                }
                else
                {
                    _bloqueos.Remove(b);
                }
            }
        }

        /// <summary>Elimina un bloqueo entero, sin partirlo.</summary>
        public void Eliminar(Bloqueo bloqueo)
        {
            _bloqueos.Remove(bloqueo);
        }

        /// <summary>Devuelve el bloqueo que contiene ese día, o null si está libre.</summary>
        public Bloqueo Buscar(DateTime dia)
        {
            dia = dia.Date;
            foreach (Bloqueo b in _bloqueos)
                if (b.FechaInicio <= dia && b.FechaFin >= dia)
                    return b;
            return null;
        }

        /// <summary>Indica si el día está ocupado o festivo.</summary>
        public bool EstaBloqueado(DateTime dia)
        {
            return Buscar(dia) != null;
        }

        /// <summary>Devuelve todos los bloqueos ordenados por fecha.</summary>
        public List<Bloqueo> Listar()
        {
            return _bloqueos.OrderBy(b => b.FechaInicio).ToList();
        }

        /// <summary>Une en uno solo los bloqueos contiguos del mismo tipo.</summary>
        private void Fusionar()
        {
            List<Bloqueo> ordenados = _bloqueos.OrderBy(b => b.FechaInicio).ToList();
            _bloqueos.Clear();

            Bloqueo actual = null;
            foreach (Bloqueo b in ordenados)
            {
                if (actual != null
                    && string.Equals(actual.Tipo, b.Tipo, StringComparison.OrdinalIgnoreCase)
                    && b.FechaInicio <= actual.FechaFin.AddDays(1))
                {
                    if (b.FechaFin > actual.FechaFin) actual.FechaFin = b.FechaFin;
                }
                else
                {
                    actual = b;
                    _bloqueos.Add(b);
                }
            }
        }

        /// <summary>Lanza un error si la fecha fin es anterior a la de inicio.</summary>
        private static void ValidarRango(DateTime inicio, DateTime fin)
        {
            if (fin.Date < inicio.Date)
                throw new ArgumentException("La fecha fin no puede ser anterior a la fecha inicio");
        }
    }

    public partial class MainWindow : Window
    {
        private const int N = 42;                  // celdas del calendario: 6 semanas x 7 días
        private const int MaxDiasRango = 3660;     // límite de seguridad (~10 años)
        private static readonly CultureInfo Es = new CultureInfo("es-ES");

        private readonly BloqueosCalendarioService _svc = new BloqueosCalendarioService();

        private readonly Border[] _cel = new Border[N];
        private readonly Border[] _numBd = new Border[N];
        private readonly TextBlock[] _num = new TextBlock[N];
        private readonly TextBlock[] _tipo = new TextBlock[N];
        private readonly DateTime[] _fecha = new DateTime[N];

        private DateTime _mes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime? _diaSel;     // día resaltado mientras la ventana "Bloqueo" está abierta
        private bool _sync;

        public MainWindow()
        {
            InitializeComponent();

            CmbTipo.ItemsSource = new List<TipoItem>
            {
                new TipoItem { Nombre = "Ocupado", Valor = BloqueosCalendarioService.TipoOcupado, Color = Paleta.Ocupado },
                new TipoItem { Nombre = "Festivo", Valor = BloqueosCalendarioService.TipoFestivo, Color = Paleta.Festivo }
            };
            CmbTipo.SelectedIndex = 0;

            ConstruirCeldas();
            Refrescar();
            Estado("Haz clic en un día para bloquearlo o desbloquearlo. Cambia de mes con las flechas ‹ ›.");
        }

        /// <summary>Indica si el día es el que tiene abierta la ventana de bloqueo.</summary>
        private bool Seleccionado(DateTime d)
        {
            return _diaSel.HasValue && _diaSel.Value.Date == d.Date;
        }

        /// <summary>Crea las 42 celdas una sola vez; después Pintar solo les cambia el color y el texto.</summary>
        private void ConstruirCeldas()
        {
            for (int k = 0; k < N; k++)
            {
                int i = k;

                var cel = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(3),
                    BorderThickness = new Thickness(3),
                    BorderBrush = Brushes.Transparent,
                    Background = Paleta.Libre,
                    Cursor = Cursors.Hand
                };

                var g = new Grid { IsHitTestVisible = false };

                var numBd = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(5, 3, 0, 0)
                };
                var num = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                };
                numBd.Child = num;

                var tipo = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(10, 0, 0, 8),
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold
                };

                g.Children.Add(numBd);
                g.Children.Add(tipo);
                cel.Child = g;

                cel.MouseLeftButtonUp += (s, e) => ClicDia(_fecha[i]);
                cel.MouseEnter += (s, e) =>
                {
                    if (!Paleta.NoBloqueable(_fecha[i]) && !Seleccionado(_fecha[i]))
                        _cel[i].BorderBrush = Paleta.Hover;
                };
                cel.MouseLeave += (s, e) =>
                    _cel[i].BorderBrush = (Seleccionado(_fecha[i]) && !Paleta.NoBloqueable(_fecha[i]))
                        ? Brushes.White : Brushes.Transparent;

                _cel[i] = cel;
                _numBd[i] = numBd;
                _num[i] = num;
                _tipo[i] = tipo;
                DiasGrid.Children.Add(cel);
            }
        }

        /// <summary>Abre la ventana de bloqueo al pulsar un día (los fines de semana no hacen nada).</summary>
        private void ClicDia(DateTime d)
        {
            if (DialogoAbierto) return;
            if (Paleta.NoBloqueable(d)) return;
            AbrirDialogo(d);
        }

        /// <summary>Actualiza color, número y etiqueta de las 42 celdas según el mes mostrado y los bloqueos.</summary>
        private void Pintar()
        {
            DateTime ini = _mes.AddDays(-(((int)_mes.DayOfWeek + 6) % 7));   // la semana empieza en lunes

            for (int i = 0; i < N; i++)
            {
                DateTime d = ini.AddDays(i);
                _fecha[i] = d;

                bool finde = Paleta.EsFinde(d);
                bool noPulsable = Paleta.NoBloqueable(d);
                Bloqueo b = _svc.Buscar(d);
                bool festivo = b != null && !finde && Paleta.EsFestivo(b.Tipo);
                bool hoy = d.Date == DateTime.Today;

                _cel[i].Background = finde ? Paleta.Finde : (b != null ? Paleta.Tipo(b.Tipo) : Paleta.Libre);
                _cel[i].Opacity = d.Month == _mes.Month ? 1.0 : 0.38;
                _cel[i].BorderBrush = (Seleccionado(d) && !noPulsable) ? Brushes.White : Brushes.Transparent;
                _cel[i].Cursor = noPulsable ? Cursors.Arrow : Cursors.Hand;

                Brush fg = finde ? Paleta.TextoFinde : (festivo ? Paleta.TextoSobreAmarillo : Brushes.White);

                _num[i].Text = d.Day.ToString();
                _numBd[i].Background = hoy ? Paleta.Acento : Brushes.Transparent;
                _num[i].Foreground = hoy ? Brushes.White : fg;

                _tipo[i].Text = (b != null && !finde) ? b.Tipo : "";
                _tipo[i].Foreground = fg;
            }
        }

        /// <summary>Muestra el mes y el año encima del calendario.</summary>
        private void ActualizarTitulo()
        {
            TxtMes.Text = Es.TextInfo.ToTitleCase(_mes.ToString("MMMM yyyy", Es));
        }

        /// <summary>Repinta título y calendario.</summary>
        private void Refrescar()
        {
            ActualizarTitulo();
            Pintar();
        }

        /// <summary>Escribe un mensaje en la barra de estado de abajo.</summary>
        private void Estado(string texto)
        {
            TxtEstado.Text = texto;
        }

        /// <summary>Bloquea [a, b] saltándose los fines de semana. Devuelve los días bloqueados.</summary>
        private int AplicarBloqueo(DateTime a, DateTime b, string tipo)
        {
            int n = 0;
            DateTime? tramo = null;                       // tramo continuo de días laborables

            for (DateTime d = a; d <= b; d = d.AddDays(1))
            {
                if (Paleta.NoBloqueable(d))
                {
                    if (tramo.HasValue)
                    {
                        _svc.BloquearRango(tramo.Value, d.AddDays(-1), tipo);
                        tramo = null;
                    }
                }
                else
                {
                    if (!tramo.HasValue) tramo = d;
                    n++;
                }
            }
            if (tramo.HasValue) _svc.BloquearRango(tramo.Value, b, tipo);
            return n;
        }

        /// <summary>
        /// Desbloquea SOLO los días ocupados o festivos de [a, b].
        /// Los días libres (verdes) no se tocan. Devuelve los días desbloqueados.
        /// </summary>
        private int AplicarDesbloqueo(DateTime a, DateTime b)
        {
            int n = 0;
            for (DateTime d = a; d <= b; d = d.AddDays(1))
                if (_svc.EstaBloqueado(d)) n++;

            if (n > 0) _svc.DesbloquearRango(a, b);
            return n;
        }

        /// <summary>Si el día no se ve en el calendario, va a su mes; luego repinta.</summary>
        private void MostrarDia(DateTime a)
        {
            if (a < _fecha[0] || a > _fecha[N - 1])
                _mes = new DateTime(a.Year, a.Month, 1);
            Refrescar();
        }

        /// <summary>Indica si la ventana de bloqueo está visible.</summary>
        private bool DialogoAbierto
        {
            get { return Overlay.Visibility == Visibility.Visible; }
        }

        /// <summary>Abre la ventana de bloqueo con el día pulsado en Inicio y Fin.</summary>
        private void AbrirDialogo(DateTime dia)
        {
            _diaSel = dia;
            Pintar();

            // Si el día ya estaba bloqueado, se preselecciona su motivo
            Bloqueo actual = _svc.Buscar(dia);

            _sync = true;
            DpInicio.SelectedDate = dia;
            DpFin.SelectedDate = dia;
            CmbTipo.SelectedIndex = (actual != null && Paleta.EsFestivo(actual.Tipo)) ? 1 : 0;
            _sync = false;

            ActualizarResumenDialogo();
            Overlay.Visibility = Visibility.Visible;
        }

        /// <summary>Cierra la ventana de bloqueo y quita el resalte del día.</summary>
        private void CerrarDialogo()
        {
            DpInicio.IsDropDownOpen = false;
            DpFin.IsDropDownOpen = false;
            Overlay.Visibility = Visibility.Collapsed;
            _diaSel = null;
            Pintar();
        }

        /// <summary>Texto resumen + estado de los botones según las fechas elegidas.</summary>
        private void ActualizarResumenDialogo()
        {
            DateTime? a = DpInicio.SelectedDate;
            DateTime? b = DpFin.SelectedDate;

            BtnDlgDesbloquear.Visibility = Visibility.Collapsed;

            if (!a.HasValue || !b.HasValue)
            {
                TxtResumen.Foreground = Paleta.Error;
                TxtResumen.Text = "Elige la fecha de inicio y la de fin.";
                BtnBloquear.IsEnabled = false;
                return;
            }
            if (b.Value.Date < a.Value.Date)
            {
                TxtResumen.Foreground = Paleta.Error;
                TxtResumen.Text = "La fecha fin no puede ser anterior a la fecha inicio.";
                BtnBloquear.IsEnabled = false;
                return;
            }
            if ((b.Value.Date - a.Value.Date).TotalDays > MaxDiasRango)
            {
                TxtResumen.Foreground = Paleta.Error;
                TxtResumen.Text = "El rango es demasiado grande (máximo 10 años).";
                BtnBloquear.IsEnabled = false;
                return;
            }

            int total = 0, libres = 0, bloqueados = 0, findes = 0;
            for (DateTime d = a.Value.Date; d <= b.Value.Date; d = d.AddDays(1))
            {
                total++;
                if (Paleta.NoBloqueable(d)) findes++;
                else if (_svc.EstaBloqueado(d)) bloqueados++;
                else libres++;
            }

            string txt = string.Format("{0} día(s): {1} libre(s) · {2} bloqueado(s)", total, libres, bloqueados);
            if (findes > 0) txt += string.Format(" · {0} de fin de semana (se ignoran)", findes);
            TxtResumen.Foreground = Paleta.TextoDialogo;
            TxtResumen.Text = txt;

            BtnBloquear.IsEnabled = libres + bloqueados > 0;

            // Desbloquear solo aparece si hay días ocupados/festivos en el rango
            if (bloqueados > 0)
            {
                BtnDlgDesbloquear.Content = string.Format("Desbloquear ({0})", bloqueados);
                BtnDlgDesbloquear.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Recalcula el resumen y los botones cuando se cambia Inicio o Fin.</summary>
        private void Dp_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_sync) return;
            ActualizarResumenDialogo();
        }

        /// <summary>Cierra la ventana de bloqueo sin cambiar nada.</summary>
        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            CerrarDialogo();
        }

        /// <summary>Bloquea el rango elegido con el motivo del desplegable.</summary>
        private void BtnBloquear_Click(object sender, RoutedEventArgs e)
        {
            var tipo = CmbTipo.SelectedItem as TipoItem;
            if (tipo == null || !DpInicio.SelectedDate.HasValue || !DpFin.SelectedDate.HasValue) return;

            DateTime a = DpInicio.SelectedDate.Value.Date;
            DateTime b = DpFin.SelectedDate.Value.Date;
            if (b < a) return;

            int n = AplicarBloqueo(a, b, tipo.Valor);

            CerrarDialogo();
            MostrarDia(a);
            Estado(string.Format("{0} día(s) marcado(s) como {1}.", n, tipo.Valor));
        }

        /// <summary>Desbloquea los días ocupados o festivos del rango elegido.</summary>
        private void BtnDlgDesbloquear_Click(object sender, RoutedEventArgs e)
        {
            if (!DpInicio.SelectedDate.HasValue || !DpFin.SelectedDate.HasValue) return;

            DateTime a = DpInicio.SelectedDate.Value.Date;
            DateTime b = DpFin.SelectedDate.Value.Date;
            if (b < a) return;

            int n = AplicarDesbloqueo(a, b);

            CerrarDialogo();
            MostrarDia(a);
            Estado(string.Format("{0} día(s) desbloqueado(s).", n));
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Esc cierra la ventana de bloqueo (si no hay un calendario desplegado)
            if (e.Key == Key.Escape && DialogoAbierto && !DpInicio.IsDropDownOpen && !DpFin.IsDropDownOpen)
            {
                CerrarDialogo();
                e.Handled = true;
            }
        }

        /// <summary>Va al mes anterior.</summary>
        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            _mes = _mes.AddMonths(-1);
            Refrescar();
        }

        /// <summary>Va al mes siguiente.</summary>
        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _mes = _mes.AddMonths(1);
            Refrescar();
        }
    }
}