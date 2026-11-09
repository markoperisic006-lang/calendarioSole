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
    // ---------------------------------------------------------------
    //  Paleta de colores y utilidades
    // ---------------------------------------------------------------
    internal static class Paleta
    {
        private static SolidColorBrush B(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        public static readonly Brush Libre = B("#22A060");               // verde
        public static readonly Brush Ocupado = B("#E14040");             // rojo
        public static readonly Brush Festivo = B("#EAB308");             // amarillo
        public static readonly Brush Finde = B("#5E617A");               // gris más claro
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

        public static bool NoBloqueable(DateTime d)
        {
            return FindesNoBloqueables && EsFinde(d);
        }

        public static bool EsFestivo(string tipo)
        {
            return string.Equals(tipo, BloqueosCalendarioService.TipoFestivo, StringComparison.OrdinalIgnoreCase);
        }

        public static Brush Tipo(string tipo)
        {
            return EsFestivo(tipo) ? Festivo : Ocupado;
        }
    }

    /// <summary>Opción del desplegable "Motivo".</summary>
    public class TipoItem
    {
        public string Nombre { get; set; }   // lo que se ve
        public string Valor { get; set; }    // OCUPADO / FESTIVO
        public Brush Color { get; set; }
    }

    /// <summary>Fila de la lista "Días bloqueados".</summary>
    public class FilaBloqueo
    {
        public Bloqueo Bloqueo { get; set; }
        public string Rango { get; set; }
        public string Tipo { get; set; }
        public Brush Color { get; set; }
    }

    // ===================================================================
    //  SERVICIO DE BLOQUEOS (en memoria, sin servidor ni base de datos)
    // ===================================================================
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

        // ---------- BLOQUEAR ----------

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

        // ---------- DESBLOQUEAR ----------

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
                    b.FechaFin = desde.AddDays(-1);              // sobra solo la parte izquierda
                }
                else if (sobraDerecha)
                {
                    b.FechaInicio = hasta.AddDays(1);            // sobra solo la parte derecha
                }
                else
                {
                    _bloqueos.Remove(b);                         // queda todo dentro del rango
                }
            }
        }

        /// <summary>Elimina un bloqueo entero (botón "Desbloquear seleccionado").</summary>
        public void Eliminar(Bloqueo bloqueo)
        {
            _bloqueos.Remove(bloqueo);
        }

        // ---------- CONSULTAS ----------

        public Bloqueo Buscar(DateTime dia)
        {
            dia = dia.Date;
            foreach (Bloqueo b in _bloqueos)
                if (b.FechaInicio <= dia && b.FechaFin >= dia)
                    return b;
            return null;
        }

        public bool EstaBloqueado(DateTime dia)
        {
            return Buscar(dia) != null;
        }

        public List<Bloqueo> Listar()
        {
            return _bloqueos.OrderBy(b => b.FechaInicio).ToList();
        }

        // ---------- Internos ----------

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

        private static void ValidarRango(DateTime inicio, DateTime fin)
        {
            if (fin.Date < inicio.Date)
                throw new ArgumentException("La fecha fin no puede ser anterior a la fecha inicio");
        }
    }

    public partial class MainWindow : Window
    {
        private const int N = 42;                  // 6 semanas x 7 días
        private const int MaxDiasRango = 3660;     // límite de seguridad (~10 años)
        private static readonly CultureInfo Es = new CultureInfo("es-ES");

        private readonly BloqueosCalendarioService _svc = new BloqueosCalendarioService();

        private readonly Border[] _cel = new Border[N];
        private readonly Border[] _numBd = new Border[N];
        private readonly TextBlock[] _num = new TextBlock[N];
        private readonly TextBlock[] _tipo = new TextBlock[N];
        private readonly DateTime[] _fecha = new DateTime[N];

        private DateTime _mes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime? _ancla, _actual;
        private bool _arrastrando, _sync;

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

            // Al soltar el ratón termina la selección (clic o arrastre) y se abre "Bloqueo"
            PreviewMouseLeftButtonUp += (s, e) => TerminarSeleccion();

            _ancla = _actual = DateTime.Today;
            Refrescar();
            Estado("Haz clic en un día para bloquearlo. Arrastra o usa Shift+clic para elegir un rango.");
        }

        // =====================================================================
        //  SELECCIÓN
        // =====================================================================

        private DateTime Lo { get { return _ancla.Value <= _actual.Value ? _ancla.Value : _actual.Value; } }
        private DateTime Hi { get { return _ancla.Value <= _actual.Value ? _actual.Value : _ancla.Value; } }

        private bool Seleccionado(DateTime d)
        {
            return _ancla.HasValue && d >= Lo && d <= Hi;
        }

        private List<DateTime> DiasSel()
        {
            var l = new List<DateTime>();
            if (!_ancla.HasValue) return l;
            for (DateTime d = Lo; d <= Hi; d = d.AddDays(1)) l.Add(d);
            return l;
        }

        // =====================================================================
        //  CELDAS DEL CALENDARIO
        // =====================================================================

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

                cel.MouseLeftButtonDown += (s, e) => IniciarSeleccion(_fecha[i]);
                cel.MouseEnter += (s, e) => CeldaEnter(i);
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

        private void CeldaEnter(int i)
        {
            // Los fines de semana no reaccionan: ni resalte ni selección al arrastrar
            if (Paleta.NoBloqueable(_fecha[i])) return;

            if (_arrastrando)
            {
                if (Mouse.LeftButton != MouseButtonState.Pressed) { _arrastrando = false; return; }
                _actual = _fecha[i];
                Pintar();
                ActualizarPanel();
            }
            else if (!Seleccionado(_fecha[i]))
            {
                _cel[i].BorderBrush = Paleta.Hover;
            }
        }

        private void IniciarSeleccion(DateTime d)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            // Los fines de semana no se pueden pulsar (ni con Shift)
            if (Paleta.NoBloqueable(d)) return;

            if (shift && _ancla.HasValue) _actual = d;      // Shift+clic: amplía el rango
            else _ancla = _actual = d;

            _arrastrando = true;
            Pintar();
            ActualizarPanel();
        }

        private void TerminarSeleccion()
        {
            if (!_arrastrando) return;
            _arrastrando = false;
            // Se abre fuera del evento del ratón para evitar problemas
            Dispatcher.BeginInvoke(new Action(AbrirDialogo));
        }

        // =====================================================================
        //  PINTADO
        // =====================================================================

        private void Pintar()
        {
            DateTime ini = _mes.AddDays(-(((int)_mes.DayOfWeek + 6) % 7));   // la semana empieza en lunes

            for (int i = 0; i < N; i++)
            {
                DateTime d = ini.AddDays(i);
                _fecha[i] = d;

                bool finde = Paleta.EsFinde(d);
                Bloqueo b = _svc.Buscar(d);
                bool festivo = b != null && !finde && Paleta.EsFestivo(b.Tipo);
                bool hoy = d.Date == DateTime.Today;

                _cel[i].Background = finde ? Paleta.Finde : (b != null ? Paleta.Tipo(b.Tipo) : Paleta.Libre);
                _cel[i].Opacity = d.Month == _mes.Month ? 1.0 : 0.38;
                bool noPulsable = Paleta.NoBloqueable(d);
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

        private void ActualizarTitulo()
        {
            TxtMes.Text = Es.TextInfo.ToTitleCase(_mes.ToString("MMMM yyyy", Es));
        }

        private void ActualizarPanel()
        {
            if (!_ancla.HasValue)
            {
                TxtSel.Text = "Sin selección";
                BtnDesbloquear.IsEnabled = false;
                BtnDesbloquear.Content = "Desbloquear";
                return;
            }

            List<DateTime> dias = DiasSel().Where(d => !Paleta.NoBloqueable(d)).ToList();
            int bloqueados = dias.Count(d => _svc.EstaBloqueado(d));

            string rango = Lo == Hi
                ? Lo.ToString("ddd d MMM yyyy", Es)
                : string.Format("{0} → {1}", Lo.ToString("d MMM yyyy", Es), Hi.ToString("d MMM yyyy", Es));

            TxtSel.Text = string.Format("{0}\n{1} día(s) · {2} bloqueado(s)", rango, dias.Count, bloqueados);

            // Desbloquear solo se activa si hay días ocupados o festivos en la selección
            BtnDesbloquear.IsEnabled = bloqueados > 0;
            BtnDesbloquear.Content = bloqueados > 0
                ? string.Format("Desbloquear ({0})", bloqueados)
                : "Desbloquear";
        }

        private void RefrescarLista()
        {
            _sync = true;
            ListaBloqueos.ItemsSource = _svc.Listar().Select(b => new FilaBloqueo
            {
                Bloqueo = b,
                Rango = b.FechaInicio == b.FechaFin
                    ? b.FechaInicio.ToString("dd/MM/yyyy")
                    : b.FechaInicio.ToString("dd/MM/yyyy") + "  →  " + b.FechaFin.ToString("dd/MM/yyyy"),
                Tipo = b.Tipo,
                Color = Paleta.Tipo(b.Tipo)
            }).ToList();
            _sync = false;
        }

        /// <summary>Repinta todo: título, calendario, lista y panel.</summary>
        private void Refrescar()
        {
            ActualizarTitulo();
            Pintar();
            RefrescarLista();
            ActualizarPanel();
        }

        private void Estado(string texto)
        {
            TxtEstado.Text = texto;
        }

        // =====================================================================
        //  OPERACIONES SOBRE EL SERVICIO
        // =====================================================================

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

        /// <summary>Deja seleccionado el rango y, si no se ve, va a su mes.</summary>
        private void SeleccionarYMostrar(DateTime a, DateTime b)
        {
            _ancla = a;
            _actual = b;
            if (a < _fecha[0] || a > _fecha[N - 1])
                _mes = new DateTime(a.Year, a.Month, 1);
            Refrescar();
        }

        // =====================================================================
        //  VENTANA "BLOQUEO"
        // =====================================================================

        private bool DialogoAbierto
        {
            get { return Overlay.Visibility == Visibility.Visible; }
        }

        private void AbrirDialogo()
        {
            if (!_ancla.HasValue) return;

            List<DateTime> dias = DiasSel();
            if (dias.All(Paleta.NoBloqueable)) return;       // solo fines de semana: nada que hacer

            // Si ya hay un bloqueo en el rango, se preselecciona su motivo
            Bloqueo primero = dias.Select(d => _svc.Buscar(d)).FirstOrDefault(b => b != null);

            _sync = true;
            DpInicio.SelectedDate = Lo;
            DpFin.SelectedDate = Hi;
            CmbTipo.SelectedIndex = (primero != null && Paleta.EsFestivo(primero.Tipo)) ? 1 : 0;
            _sync = false;

            ActualizarResumenDialogo();
            Overlay.Visibility = Visibility.Visible;
        }

        private void CerrarDialogo()
        {
            DpInicio.IsDropDownOpen = false;
            DpFin.IsDropDownOpen = false;
            Overlay.Visibility = Visibility.Collapsed;
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

        private void Dp_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_sync) return;
            ActualizarResumenDialogo();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            CerrarDialogo();
        }

        private void BtnBloquear_Click(object sender, RoutedEventArgs e)
        {
            var tipo = CmbTipo.SelectedItem as TipoItem;
            if (tipo == null || !DpInicio.SelectedDate.HasValue || !DpFin.SelectedDate.HasValue) return;

            DateTime a = DpInicio.SelectedDate.Value.Date;
            DateTime b = DpFin.SelectedDate.Value.Date;
            if (b < a) return;

            int n = AplicarBloqueo(a, b, tipo.Valor);

            CerrarDialogo();
            SeleccionarYMostrar(a, b);
            Estado(string.Format("{0} día(s) marcado(s) como {1}.", n, tipo.Valor));
        }

        private void BtnDlgDesbloquear_Click(object sender, RoutedEventArgs e)
        {
            if (!DpInicio.SelectedDate.HasValue || !DpFin.SelectedDate.HasValue) return;

            DateTime a = DpInicio.SelectedDate.Value.Date;
            DateTime b = DpFin.SelectedDate.Value.Date;
            if (b < a) return;

            int n = AplicarDesbloqueo(a, b);

            CerrarDialogo();
            SeleccionarYMostrar(a, b);
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

        // =====================================================================
        //  EVENTOS DE LA PANTALLA PRINCIPAL
        // =====================================================================

        private int _rueda;   // acumula el giro (los touchpads envían muchos eventos pequeños)

        /// <summary>Rueda hacia abajo = mes siguiente · rueda hacia arriba = mes anterior.</summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (DialogoAbierto || ListaBloqueos.IsMouseOver) return;   // la lista tiene su propio scroll

            _rueda += e.Delta;
            while (_rueda <= -120) { _rueda += 120; _mes = _mes.AddMonths(1); }
            while (_rueda >= 120) { _rueda -= 120; _mes = _mes.AddMonths(-1); }

            ActualizarTitulo();
            Pintar();
            e.Handled = true;
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            _mes = _mes.AddMonths(-1);
            ActualizarTitulo();
            Pintar();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _mes = _mes.AddMonths(1);
            ActualizarTitulo();
            Pintar();
        }

        private void BtnHoy_Click(object sender, RoutedEventArgs e)
        {
            _mes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            _ancla = _actual = DateTime.Today;
            ActualizarTitulo();
            Pintar();
            ActualizarPanel();
        }

        private void ListaBloqueos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_sync) return;
            var f = ListaBloqueos.SelectedItem as FilaBloqueo;
            if (f == null) return;

            _ancla = f.Bloqueo.FechaInicio;
            _actual = f.Bloqueo.FechaFin;
            _mes = new DateTime(f.Bloqueo.FechaInicio.Year, f.Bloqueo.FechaInicio.Month, 1);
            ActualizarTitulo();
            Pintar();
            ActualizarPanel();
        }

        private void BtnDesbloquear_Click(object sender, RoutedEventArgs e)
        {
            if (!_ancla.HasValue) return;

            DateTime a = Lo, b = Hi;
            int n = AplicarDesbloqueo(a, b);

            if (n == 0) { Estado("En la selección no hay días ocupados ni festivos."); return; }

            Refrescar();
            Estado(string.Format("{0} día(s) desbloqueado(s).", n));
        }
    }
}