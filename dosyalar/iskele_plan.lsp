;;; ------------------------------------------------------------
;;; iskele_plan.lsp
;;; Kapali poligonun etrafina kalip iskele plani olusturur.
;;; Komut: ISKELEPLAN
;;; ------------------------------------------------------------

(vl-load-com)

;;; ============ YARDIMCI FONKSIYONLAR ============

(defun _isk:lwpoly-pts (ename / ed pts)
  (setq ed (entget ename))
  (foreach d ed
    (if (= (car d) 10)
      (setq pts (cons (list (cadr d) (caddr d) 0.0) pts))
    )
  )
  (reverse pts)
)

(defun _isk:centroid-avg (pts / sx sy n)
  (setq sx 0.0 sy 0.0 n 0)
  (foreach p pts
    (setq sx (+ sx (car p))
          sy (+ sy (cadr p))
          n  (1+ n))
  )
  (if (> n 0)
    (list (/ sx n) (/ sy n) 0.0)
    '(0.0 0.0 0.0)
  )
)

(defun _isk:v+ (a b) (mapcar '+ a b))
(defun _isk:v- (a b) (mapcar '- a b))
(defun _isk:v* (v s) (mapcar '(lambda (x) (* x s)) v))

(defun _isk:vunit (v / l)
  (setq l (distance '(0.0 0.0 0.0) v))
  (if (> l 1e-12) (_isk:v* v (/ 1.0 l)) '(1.0 0.0 0.0))
)

;;; Signed area: pozitif = CCW, negatif = CW
(defun _isk:signed-area (pts / n i x1 y1 x2 y2 sa)
  (setq n (length pts) sa 0.0 i 0)
  (while (< i n)
    (setq x1 (car (nth i pts))
          y1 (cadr (nth i pts))
          x2 (car (nth (rem (1+ i) n) pts))
          y2 (cadr (nth (rem (1+ i) n) pts)))
    (setq sa (+ sa (- (* x1 y2) (* x2 y1))))
    (setq i (1+ i))
  )
  (/ sa 2.0)
)

;;; Nokta, listedeki herhangi bir noktaya minDist'den yakin mi?
(defun _isk:too-close-to-any (pt ptList minDist / tooClose)
  (setq tooClose nil)
  (foreach v ptList
    (if (and (not tooClose) (< (distance pt v) minDist))
      (setq tooClose T)
    )
  )
  tooClose
)

;;; Teget dikme noktasi atlanabilir mi?
;;; 20cm vertex yakinlik kontrolu + 250cm bossluk guvenligi
;;; T donerse nokta atlanabilir, nil donerse dikme kalsin
(defun _isk:can-skip-tangent (pOut polyPts allVertexPts /
  dPt totalLen prevD nextD gapWithout vDists n i d)
  (if (not (_isk:too-close-to-any pOut allVertexPts 20.0))
    nil
    (progn
      (setq dPt (_isk:dist-along-poly pOut polyPts))
      (setq totalLen (_isk:total-poly-len polyPts))
      (setq vDists nil)
      (foreach v polyPts
        (setq vDists (cons (_isk:dist-along-poly v polyPts) vDists))
      )
      (setq vDists (vl-sort vDists '<))
      (setq n (length vDists))
      (setq prevD nil i 0)
      (while (< i n)
        (setq d (nth i vDists))
        (if (< d (- dPt 0.5)) (setq prevD d))
        (setq i (1+ i))
      )
      (if (null prevD) (setq prevD (nth (1- n) vDists)))
      (setq nextD nil i 0)
      (while (and (< i n) (null nextD))
        (setq d (nth i vDists))
        (if (> d (+ dPt 0.5)) (setq nextD d))
        (setq i (1+ i))
      )
      (if (null nextD) (setq nextD (nth 0 vDists)))
      (if (> nextD prevD)
        (setq gapWithout (- nextD prevD))
        (setq gapWithout (+ (- totalLen prevD) nextD))
      )
      (<= gapWithout 250.0)
    )
  )
)

;;; ============ DOGRUSAL VERTEX TEMIZLEME ============

(defun _isk:remove-collinear (pts / n i p1 p2 p3 d1x d1y d2x d2y len1 len2
                                    cross dot result)
  ;; Ardisik iki segmentin YON vektorlerini karsilastirir.
  ;; Ayni yone gidiyorlarsa (cross~0, dot>0) vertex gereksiz => sil.
  (setq n (length pts))
  (if (<= n 3) pts
    (progn
      (setq result nil i 0)
      (repeat n
        (setq p1 (nth (rem (+ i n -1) n) pts)
              p2 (nth i pts)
              p3 (nth (rem (1+ i) n) pts))
        ;; Segment 1 yonu: p1 -> p2
        (setq d1x (- (car p2) (car p1))
              d1y (- (cadr p2) (cadr p1))
              len1 (sqrt (+ (* d1x d1x) (* d1y d1y))))
        ;; Segment 2 yonu: p2 -> p3
        (setq d2x (- (car p3) (car p2))
              d2y (- (cadr p3) (cadr p2))
              len2 (sqrt (+ (* d2x d2x) (* d2y d2y))))
        (if (and (> len1 0.001) (> len2 0.001))
          (progn
            ;; Birim vektorlerin cross ve dot carpimi
            (setq cross (abs (- (* (/ d1x len1) (/ d2y len2))
                                (* (/ d1y len1) (/ d2x len2)))))
            (setq dot (+ (* (/ d1x len1) (/ d2x len2))
                         (* (/ d1y len1) (/ d2y len2))))
          )
          (progn (setq cross 999.0 dot 0.0))
        )
        ;; cross < 0.05 => segmentler paralel (~3 derece tolerans)
        ;; dot > 0     => ayni yone gidiyor (geri donmus degil)
        (if (and (< cross 0.05) (> dot 0.0))
          (prompt (strcat "\n  Vertex silindi: ("
                          (rtos (car p2) 2 1) "," (rtos (cadr p2) 2 1)
                          ") cross=" (rtos cross 2 6)
                          " dot=" (rtos dot 2 4)))
          (setq result (cons p2 result))
        )
        (setq i (1+ i))
      )
      (setq result (reverse result))
      (if (< (length result) 3) pts result)
    )
  )
)

;;; ============ DIK IZDUSUMU (SEGMENT UZERINDE) ============

(defun _isk:perp-foot-on-seg (pt p1 p2 / dx dy len2 t1)
  ;; pt'den p1-p2 segmentine dik izdusumu dondurur, segment disindaysa nil
  (setq dx (- (car p2) (car p1))
        dy (- (cadr p2) (cadr p1))
        len2 (+ (* dx dx) (* dy dy)))
  (if (< len2 1e-12) nil
    (progn
      (setq t1 (/ (+ (* (- (car pt) (car p1)) dx)
                     (* (- (cadr pt) (cadr p1)) dy)) len2))
      (if (and (>= t1 0.0) (<= t1 1.0))
        (list (+ (car p1) (* t1 dx)) (+ (cadr p1) (* t1 dy)) 0.0)
        nil
      )
    )
  )
)

(defun _isk:closest-perp-on-poly (pt polyPts / n i p1 p2 foot bestFoot bestDist d)
  ;; pt'den poligon segmentlerine en yakin dik izdusumu (kose degil, her zaman dik)
  (setq n (length polyPts) bestDist 1e30 bestFoot nil i 0)
  (while (< i n)
    (setq p1 (nth i polyPts)
          p2 (nth (rem (1+ i) n) polyPts))
    (setq foot (_isk:perp-foot-on-seg pt p1 p2))
    (if foot
      (progn
        (setq d (distance pt foot))
        (if (< d bestDist)
          (setq bestDist d bestFoot foot)
        )
      )
    )
    (setq i (1+ i))
  )
  bestFoot
)

(defun _isk:find-seg-idx (pt polyPts / n i p1 p2 result)
  ;; pt'nin hangi poligon segmentinde oldugunu dondurur (indeks)
  (setq n (length polyPts) i 0 result 0)
  (while (< i n)
    (setq p1 (nth i polyPts)
          p2 (nth (rem (1+ i) n) polyPts))
    (if (_isk:pt-on-seg? pt p1 p2 1.0)
      (progn (setq result i) (setq i n))
      (setq i (1+ i))
    )
  )
  result
)

;;; ============ DAIRE - SEGMENT KESISIM (SAF GEOMETRI) ============

(defun _isk:circle-seg-tangent (center radius p1 p2 / dx dy fx fy a b c disc t1 pts pt)
  ;; Sadece TEGET noktasini dondurur (disc ~ 0)
  (setq dx (- (car p2) (car p1))
        dy (- (cadr p2) (cadr p1))
        fx (- (car p1) (car center))
        fy (- (cadr p1) (cadr center)))
  (setq a (+ (* dx dx) (* dy dy)))
  (if (< a 1e-12) (setq a 1e-12))
  (setq b (* 2.0 (+ (* fx dx) (* fy dy)))
        c (- (+ (* fx fx) (* fy fy)) (* radius radius)))
  (setq disc (- (* b b) (* 4.0 a c)))
  (setq pts nil)
  ;; disc ~ 0 ise teget (tolerans: disc/4a < 25 cm^2 => yaklasik 5cm sapma)
  (if (and (>= disc (* -100.0 a)) (<= disc (* 100.0 a)))
    (progn
      (setq t1 (/ (- 0.0 b) (* 2.0 a)))
      (if (and (>= t1 -0.01) (<= t1 1.01))
        (progn
          (setq t1 (max 0.0 (min 1.0 t1)))
          (setq pt (list (+ (car p1) (* t1 dx)) (+ (cadr p1) (* t1 dy)) 0.0))
          (setq pts (cons pt pts))
        )
      )
    )
  )
  pts
)

(defun _isk:circle-poly-tangents (center radius polyPts / n i p1 p2 segHits all)
  (setq n (length polyPts))
  (setq all nil)
  (setq i 0)
  (while (< i n)
    (setq p1 (nth i polyPts))
    (setq p2 (nth (rem (1+ i) n) polyPts))
    (setq segHits (_isk:circle-seg-tangent center radius p1 p2))
    (foreach h segHits
      (setq all (cons h all))
    )
    (setq i (1+ i))
  )
  (reverse all)
)

;;; ============ POLIGON UZERINDE MESAFE / NORMAL ============

(defun _isk:pt-on-seg? (pt p1 p2 tol / dx dy t1 proj)
  (setq dx (- (car p2) (car p1))
        dy (- (cadr p2) (cadr p1)))
  (setq t1 (+ (* dx dx) (* dy dy)))
  (if (< t1 1e-12) nil
    (progn
      (setq proj (/ (+ (* (- (car pt) (car p1)) dx) (* (- (cadr pt) (cadr p1)) dy)) t1))
      (and (>= proj (- 0.0 0.01)) (<= proj 1.01)
           (< (distance pt (list (+ (car p1) (* proj dx)) (+ (cadr p1) (* proj dy)) 0.0)) tol))
    )
  )
)

(defun _isk:dist-along-poly (pt polyPts / n i p1 p2 cumul)
  (setq n (length polyPts) i 0 cumul 0.0)
  (while (< i n)
    (setq p1 (nth i polyPts)
          p2 (nth (rem (1+ i) n) polyPts))
    (if (_isk:pt-on-seg? pt p1 p2 2.0)
      (progn
        (setq cumul (+ cumul (distance p1 pt)))
        (setq i n)
      )
      (progn
        (setq cumul (+ cumul (distance p1 p2)))
        (setq i (1+ i))
      )
    )
  )
  cumul
)

(defun _isk:total-poly-len (polyPts / n i len)
  (setq n (length polyPts) i 0 len 0.0)
  (while (< i n)
    (setq len (+ len (distance (nth i polyPts) (nth (rem (1+ i) n) polyPts))))
    (setq i (1+ i))
  )
  len
)

(defun _isk:pt-at-dist-on-poly (dist polyPts / n i p1 p2 segL cumul rem2 dir)
  (setq n (length polyPts) i 0 cumul 0.0)
  (while (< i n)
    (setq p1 (nth i polyPts)
          p2 (nth (rem (1+ i) n) polyPts)
          segL (distance p1 p2))
    (if (<= dist (+ cumul segL 0.01))
      (progn
        (setq rem2 (- dist cumul))
        (if (< rem2 0.0) (setq rem2 0.0))
        (if (> rem2 segL) (setq rem2 segL))
        (setq dir (_isk:vunit (_isk:v- p2 p1)))
        (setq i n)
        (setq p1 (_isk:v+ p1 (_isk:v* dir rem2)))
      )
      (progn
        (setq cumul (+ cumul segL))
        (setq i (1+ i))
        (if (>= i n) (setq p1 (car polyPts)))
      )
    )
  )
  p1
)

(defun _isk:outward-normal-at (pt polyPts cen / n i p1 p2 nrm sa)
  ;; Signed area ile donus yonunu belirle
  (setq sa (_isk:signed-area polyPts))
  (setq n (length polyPts) i 0)
  (while (< i n)
    (setq p1 (nth i polyPts)
          p2 (nth (rem (1+ i) n) polyPts))
    (if (_isk:pt-on-seg? pt p1 p2 2.0)
      (progn
        ;; Sol normal (-dy, dx): p1->p2 kenarinin soluna isaret eder
        (setq nrm (_isk:vunit (list (- (cadr p1) (cadr p2)) (- (car p2) (car p1)) 0.0)))
        ;; CCW (sa>0): sol normal iceri bakar -> flip ile disa dondir
        ;; CW  (sa<0): sol normal disa bakar -> degistirme
        (if (> sa 0.0) (setq nrm (_isk:v* nrm -1.0)))
        (setq i n)
      )
      (setq i (1+ i))
    )
  )
  (if (null nrm) (setq nrm (_isk:vunit (_isk:v- pt cen))))
  nrm
)

;;; ============ DIKME SEMBOL (DXF birebir) ============

(defun _isk:rot2d (px py ang / c s)
  (setq c (cos ang) s (sin ang))
  (list (- (* px c) (* py s))
        (+ (* px s) (* py c)))
)

(defun _isk:draw-dikme (ms pt / cx cy bR i posAng rotAng bx by rp elist curLay)
  (setq cx (car pt) cy (cadr pt))
  (setq curLay (getvar "CLAYER"))

  ;; 3 ana daire (flans R=6.5, boru dis R=2.8, boru ic R=2.5)
  (vla-AddCircle ms (vlax-3d-point pt) 6.5)
  (vla-AddCircle ms (vlax-3d-point pt) 2.8)
  (vla-AddCircle ms (vlax-3d-point pt) 2.5)

  ;; 4 busluk (polar array 90 derece)
  ;; DXF: busluk merkeze 4.337 uzaklikta
  ;; Pozisyon acilari: 90, 180, 270, 360(0)
  ;; U5 rotasyonlari:  0,  90, 180, 270
  (setq bR 4.337)
  (setq i 0)
  (repeat 4
    (setq posAng (* (+ 90.0 (* i 90.0)) (/ pi 180.0)))
    (setq rotAng (* (* i 90.0) (/ pi 180.0)))
    (setq bx (+ cx (* bR (cos posAng))))
    (setq by (+ cy (* bR (sin posAng))))

    ;; Busluk dairesi: DXF center (-0.000731, 0.134217) R=0.9978
    (setq rp (_isk:rot2d -0.000731 0.134217 rotAng))
    (vla-AddCircle ms
      (vlax-3d-point (list (+ bx (car rp)) (+ by (cadr rp)) 0.0))
      0.9978)

    ;; Busluk polyline (5 vertex, acik, bulge'li yaylar)
    ;; DXF'ten birebir vertex koordinatlari ve bulge degerleri
    (setq elist
      (list '(0 . "LWPOLYLINE")
            '(100 . "AcDbEntity")
            (cons 8 curLay)
            '(100 . "AcDbPolyline")
            '(90 . 5)
            '(70 . 0)
            '(43 . 0.0)))

    ;; V1: (-2.103918, 0.713340) bulge=0
    (setq rp (_isk:rot2d -2.103918 0.713340 rotAng))
    (setq elist (append elist
      (list (cons 10 (list (+ bx (car rp)) (+ by (cadr rp))))
            '(42 . 0.0))))

    ;; V2: (-1.339525, -1.132067) bulge=-0.200347 (yay v2->v3)
    (setq rp (_isk:rot2d -1.339525 -1.132067 rotAng))
    (setq elist (append elist
      (list (cons 10 (list (+ bx (car rp)) (+ by (cadr rp))))
            (cons 42 -0.200347))))

    ;; V3: (1.336909, -1.131413) bulge=0
    (setq rp (_isk:rot2d 1.336909 -1.131413 rotAng))
    (setq elist (append elist
      (list (cons 10 (list (+ bx (car rp)) (+ by (cadr rp))))
            '(42 . 0.0))))

    ;; V4: (2.103918, 0.715993) bulge=0.198391 (yay v4->v5)
    (setq rp (_isk:rot2d 2.103918 0.715993 rotAng))
    (setq elist (append elist
      (list (cons 10 (list (+ bx (car rp)) (+ by (cadr rp))))
            (cons 42 0.198391))))

    ;; V5: (-2.103918, 0.713340) bulge=0
    (setq rp (_isk:rot2d -2.103918 0.713340 rotAng))
    (setq elist (append elist
      (list (cons 10 (list (+ bx (car rp)) (+ by (cadr rp))))
            '(42 . 0.0))))

    (entmakex elist)
    (setq i (1+ i))
  )
)

;;; ============ YATAY ELEMAN (cift paralel cizgi) ============

(defun _isk:draw-yatay (ms p1 p2 / dir perp sp ep s1 e1 s2 e2 d)
  ;; p1, p2: dikme merkez noktalari
  ;; Iki paralel cizgi cizip, +-2.5 offset, her uctan 6.0 iceri
  (setq d (distance p1 p2))
  (if (> d 13.0)
    (progn
      (setq dir (_isk:vunit (_isk:v- p2 p1)))
      (setq perp (list (- (cadr dir)) (car dir) 0.0))
      (setq sp (_isk:v+ p1 (_isk:v* dir 6.0)))
      (setq ep (_isk:v- p2 (_isk:v* dir 6.0)))
      (setq s1 (_isk:v+ sp (_isk:v* perp 2.5)))
      (setq e1 (_isk:v+ ep (_isk:v* perp 2.5)))
      (vla-AddLine ms (vlax-3d-point s1) (vlax-3d-point e1))
      (setq s2 (_isk:v- sp (_isk:v* perp 2.5)))
      (setq e2 (_isk:v- ep (_isk:v* perp 2.5)))
      (vla-AddLine ms (vlax-3d-point s2) (vlax-3d-point e2))
    )
  )
)

;;; ============ ANKRAJ SEMBOL (DXF birebir) ============

(defun _isk:draw-ankraj (ms dikmePt buildPt / dir perp d sp
                          bracketBot bracketTop bracketCen
                          s1 e1 s2 e2
                          bl br tl tr
                          boltC bTop bBot
                          eL eR curLay)
  ;; dikmePt: dikme merkezi (ilk poligon uzerinde)
  ;; buildPt: baglanti noktasi (ic poligon uzerinde)
  (setq d (distance dikmePt buildPt))
  (if (< d 8.0) nil
    (progn
      (setq dir (_isk:vunit (_isk:v- buildPt dikmePt)))
      (setq perp (list (- (cadr dir)) (car dir) 0.0))
      (setq curLay (getvar "CLAYER"))

      ;; Bracket konumlari
      (setq bracketTop buildPt)
      (setq bracketBot (_isk:v- buildPt (_isk:v* dir 0.8)))
      (setq bracketCen (_isk:v- buildPt (_isk:v* dir 0.4)))

      ;; Tube cizgileri (+-2.5 offset, dikme+6 den bracket alt kenarına)
      (setq sp (_isk:v+ dikmePt (_isk:v* dir 6.0)))
      (setq s1 (_isk:v+ sp (_isk:v* perp 2.5)))
      (setq e1 (_isk:v+ bracketBot (_isk:v* perp 2.5)))
      (vla-AddLine ms (vlax-3d-point s1) (vlax-3d-point e1))
      (setq s2 (_isk:v- sp (_isk:v* perp 2.5)))
      (setq e2 (_isk:v- bracketBot (_isk:v* perp 2.5)))
      (vla-AddLine ms (vlax-3d-point s2) (vlax-3d-point e2))

      ;; Bracket dikdortgen (15 genis x 0.8 yuksek)
      (setq bl (_isk:v- bracketBot (_isk:v* perp 7.5)))
      (setq br (_isk:v+ bracketBot (_isk:v* perp 7.5)))
      (setq tl (_isk:v- bracketTop (_isk:v* perp 7.5)))
      (setq tr (_isk:v+ bracketTop (_isk:v* perp 7.5)))
      (vla-AddLine ms (vlax-3d-point bl) (vlax-3d-point br))
      (vla-AddLine ms (vlax-3d-point tl) (vlax-3d-point tr))
      (vla-AddLine ms (vlax-3d-point bl) (vlax-3d-point tl))
      (vla-AddLine ms (vlax-3d-point br) (vlax-3d-point tr))

      ;; --- Civatalar ISKELE BOLT katmaninda ---
      (setvar "CLAYER" "ISKELE BOLT (BEYKENT)")

      ;; --- Sol civata (bolt) : -4.0 perp ---
      (setq boltC (_isk:v- bracketCen (_isk:v* perp 4.0)))
      (setq bTop (_isk:v+ boltC (_isk:v* dir 1.75)))
      (setq bBot (_isk:v- boltC (_isk:v* dir 1.75)))
      (vla-AddLine ms (vlax-3d-point bTop) (vlax-3d-point bBot))
      (setq eL (_isk:v- boltC (_isk:v* perp 0.9)))
      (setq eR (_isk:v+ boltC (_isk:v* perp 0.9)))
      (vla-AddLine ms
        (vlax-3d-point (_isk:v+ eL (_isk:v* dir 0.4)))
        (vlax-3d-point (_isk:v- eL (_isk:v* dir 0.4))))
      (vla-AddLine ms
        (vlax-3d-point (_isk:v+ eR (_isk:v* dir 0.4)))
        (vlax-3d-point (_isk:v- eR (_isk:v* dir 0.4))))
      (entmakex
        (list '(0 . "SOLID")
              (cons 8 "ISKELE BOLT (BEYKENT)")
              (cons 10 (list (car (_isk:v+ eL (_isk:v* dir 0.4)))
                             (cadr (_isk:v+ eL (_isk:v* dir 0.4))) 0.0))
              (cons 11 (list (car (_isk:v+ eR (_isk:v* dir 0.4)))
                             (cadr (_isk:v+ eR (_isk:v* dir 0.4))) 0.0))
              (cons 12 (list (car (_isk:v- eL (_isk:v* dir 0.4)))
                             (cadr (_isk:v- eL (_isk:v* dir 0.4))) 0.0))
              (cons 13 (list (car (_isk:v- eR (_isk:v* dir 0.4)))
                             (cadr (_isk:v- eR (_isk:v* dir 0.4))) 0.0))))

      ;; --- Sag civata (bolt) : +4.0 perp ---
      (setq boltC (_isk:v+ bracketCen (_isk:v* perp 4.0)))
      (setq bTop (_isk:v+ boltC (_isk:v* dir 1.75)))
      (setq bBot (_isk:v- boltC (_isk:v* dir 1.75)))
      (vla-AddLine ms (vlax-3d-point bTop) (vlax-3d-point bBot))
      (setq eL (_isk:v- boltC (_isk:v* perp 0.9)))
      (setq eR (_isk:v+ boltC (_isk:v* perp 0.9)))
      (vla-AddLine ms
        (vlax-3d-point (_isk:v+ eL (_isk:v* dir 0.4)))
        (vlax-3d-point (_isk:v- eL (_isk:v* dir 0.4))))
      (vla-AddLine ms
        (vlax-3d-point (_isk:v+ eR (_isk:v* dir 0.4)))
        (vlax-3d-point (_isk:v- eR (_isk:v* dir 0.4))))
      (entmakex
        (list '(0 . "SOLID")
              (cons 8 "ISKELE BOLT (BEYKENT)")
              (cons 10 (list (car (_isk:v+ eL (_isk:v* dir 0.4)))
                             (cadr (_isk:v+ eL (_isk:v* dir 0.4))) 0.0))
              (cons 11 (list (car (_isk:v+ eR (_isk:v* dir 0.4)))
                             (cadr (_isk:v+ eR (_isk:v* dir 0.4))) 0.0))
              (cons 12 (list (car (_isk:v- eL (_isk:v* dir 0.4)))
                             (cadr (_isk:v- eL (_isk:v* dir 0.4))) 0.0))
              (cons 13 (list (car (_isk:v- eR (_isk:v* dir 0.4)))
                             (cadr (_isk:v- eR (_isk:v* dir 0.4))) 0.0))))

      ;; Ankraj katmanina geri don
      (setvar "CLAYER" curLay)
    )
  )
)

;;; ============ YATAY ELEMAN DETAY (1:10 OLCEK, 5x BUYUK) ============

(defun _isk:draw-yatay-detay (ms basePt lengthCm label /
  H cx by R Ri fHW fH eRS eRE eRiS eRiE bT bI
  curLay dimObj txtObj dimY)

  (setq H (* lengthCm 5.0))
  (setq cx (car basePt) by (cadr basePt))

  ;; DXF'ten birebir alinan sabitler (5x plan olcegi)
  (setq R 12.075 Ri 10.825 fHW 1.25 fH 20.0)
  (setq eRS 5.0 eRE 15.0 eRiS 4.696 eRiE 15.303)
  (setq bT -0.25 bI -0.2357)

  (setq curLay (getvar "CLAYER"))

  ;; --- Dis profil (20 vertex, kapali, Part katmani stili) ---
  (setvar "CLAYER" "ISKELE DETAY (BEYKENT)")
  (entmakex
    (list '(0 . "LWPOLYLINE") '(100 . "AcDbEntity")
          '(8 . "ISKELE DETAY (BEYKENT)")
          '(100 . "AcDbPolyline") '(90 . 20) '(70 . 1) '(43 . 0.0)
          (cons 10 (list (- cx fHW) (+ by fH)))         '(42 . 0.0)
          (cons 10 (list (+ cx fHW) (+ by fH)))         '(42 . 0.0)
          (cons 10 (list (+ cx fHW) by))                 '(42 . 0.0)
          (cons 10 (list (+ cx R) by))                   '(42 . 0.0)
          (cons 10 (list (+ cx R) (+ by eRS)))           (cons 42 bT)
          (cons 10 (list (+ cx R) (+ by eRE)))           '(42 . 0.0)
          (cons 10 (list (+ cx R) (+ by (- H eRE))))     (cons 42 bT)
          (cons 10 (list (+ cx R) (+ by (- H eRS))))     '(42 . 0.0)
          (cons 10 (list (+ cx R) (+ by H)))             '(42 . 0.0)
          (cons 10 (list (+ cx fHW) (+ by H)))           '(42 . 0.0)
          (cons 10 (list (+ cx fHW) (+ by (- H fH))))   '(42 . 0.0)
          (cons 10 (list (- cx fHW) (+ by (- H fH))))   '(42 . 0.0)
          (cons 10 (list (- cx fHW) (+ by H)))           '(42 . 0.0)
          (cons 10 (list (- cx R) (+ by H)))             '(42 . 0.0)
          (cons 10 (list (- cx R) (+ by (- H eRS))))     (cons 42 bT)
          (cons 10 (list (- cx R) (+ by (- H eRE))))     '(42 . 0.0)
          (cons 10 (list (- cx R) (+ by eRE)))           (cons 42 bT)
          (cons 10 (list (- cx R) (+ by eRS)))           '(42 . 0.0)
          (cons 10 (list (- cx R) by))                   '(42 . 0.0)
          (cons 10 (list (- cx fHW) by))                 '(42 . 0.0)
    ))

  ;; --- Ic profil (12 vertex, kapali, gizli cizgi stili) ---
  (setvar "CLAYER" "ISKELE DETAY GIZLI (BEYKENT)")
  (entmakex
    (list '(0 . "LWPOLYLINE") '(100 . "AcDbEntity")
          '(8 . "ISKELE DETAY GIZLI (BEYKENT)")
          '(100 . "AcDbPolyline") '(90 . 12) '(70 . 1) '(43 . 0.0)
          (cons 10 (list (+ cx Ri) by))                   '(42 . 0.0)
          (cons 10 (list (+ cx Ri) (+ by eRiS)))         (cons 42 bI)
          (cons 10 (list (+ cx Ri) (+ by eRiE)))         '(42 . 0.0)
          (cons 10 (list (+ cx Ri) (+ by (- H eRiE))))   (cons 42 bI)
          (cons 10 (list (+ cx Ri) (+ by (- H eRiS))))   '(42 . 0.0)
          (cons 10 (list (+ cx Ri) (+ by H)))             '(42 . 0.0)
          (cons 10 (list (- cx Ri) (+ by H)))             '(42 . 0.0)
          (cons 10 (list (- cx Ri) (+ by (- H eRiS))))   (cons 42 bI)
          (cons 10 (list (- cx Ri) (+ by (- H eRiE))))   '(42 . 0.0)
          (cons 10 (list (- cx Ri) (+ by eRiE)))         (cons 42 bI)
          (cons 10 (list (- cx Ri) (+ by eRiS)))         '(42 . 0.0)
          (cons 10 (list (- cx Ri) by))                   '(42 . 0.0)
    ))

  ;; --- Boyut olcusu: boy (dikey, sag taraf, 40 birim saga offset) ---
  (setvar "CLAYER" "OLCU (BEYKENT)")
  (command "_.-DIMSTYLE" "_R" "PLAN_OLCU_DETAY")
  (setq dimObj (vl-catch-all-apply 'vla-AddDimAligned
    (list ms
      (vlax-3d-point (list (+ cx R) by 0.0))
      (vlax-3d-point (list (+ cx R) (+ by H) 0.0))
      (vlax-3d-point (list (+ cx R 40.0) (+ by (* H 0.5)) 0.0)))))
  (if (not (vl-catch-all-error-p dimObj))
    (vla-put-TextOverride dimObj (rtos (* lengthCm 10.0) 2 0)))

  ;; --- Boyut olcusu: cap (yatay, boru capi) ---
  (setq dimY (+ by (* H 0.6)))
  (setq dimObj (vl-catch-all-apply 'vla-AddDimAligned
    (list ms
      (vlax-3d-point (list (- cx R) dimY 0.0))
      (vlax-3d-point (list (+ cx R) dimY 0.0))
      (vlax-3d-point (list cx (+ dimY 15.0) 0.0)))))
  (if (not (vl-catch-all-error-p dimObj))
    (vla-put-TextOverride dimObj "48.3"))

  ;; --- Etiket yazisi (90 derece donuk, sol tarafta) ---
  (setvar "CLAYER" "YAZI (BEYKENT)")
  (setq txtObj (vla-AddText ms
    (strcat label " (D48.3/3.2)")
    (vlax-3d-point (list (- cx R 14.0) (+ by 82.0) 0.0))
    15.0))
  (vla-put-Rotation txtObj (* 90.0 (/ pi 180.0)))
  (vl-catch-all-apply 'vla-put-StyleName (list txtObj "YAZI (BEYKENT)"))

  (setvar "CLAYER" curLay)
)

;;; ============ DIKME DETAY (D1/D2, 1:10 OLCEK) ============

(defun _isk:draw-dikme-detay (ms basePt tubeH tubeStartDy plateYs hasBase label /
  cx by R Ri flangeR flangeRi plateHW flangeStart flangeEnd
  curLay dimObj txtObj totalH py)

  (setq cx (car basePt) by (cadr basePt))
  (setq R 12.075 Ri 10.825 flangeR 10.825 flangeRi 9.575 plateHW 32.075)
  (setq flangeStart (+ tubeStartDy tubeH -25.0))
  (setq flangeEnd (+ tubeStartDy tubeH 100.0))
  (setq totalH flangeEnd)
  (setq curLay (getvar "CLAYER"))

  ;; --- Dis boru profili (dikdortgen, 4pt) ---
  (setvar "CLAYER" "ISKELE DETAY (BEYKENT)")
  (entmakex
    (list '(0 . "LWPOLYLINE") '(100 . "AcDbEntity")
          '(8 . "ISKELE DETAY (BEYKENT)")
          '(100 . "AcDbPolyline") '(90 . 4) '(70 . 1) '(43 . 0.0)
          (cons 10 (list (- cx R) (+ by tubeStartDy)))       '(42 . 0.0)
          (cons 10 (list (+ cx R) (+ by tubeStartDy)))       '(42 . 0.0)
          (cons 10 (list (+ cx R) (+ by tubeStartDy tubeH))) '(42 . 0.0)
          (cons 10 (list (- cx R) (+ by tubeStartDy tubeH))) '(42 . 0.0)))

  ;; --- Ic boru profili (gizli) ---
  (setvar "CLAYER" "ISKELE DETAY GIZLI (BEYKENT)")
  (entmakex
    (list '(0 . "LWPOLYLINE") '(100 . "AcDbEntity")
          '(8 . "ISKELE DETAY GIZLI (BEYKENT)")
          '(100 . "AcDbPolyline") '(90 . 4) '(70 . 1) '(43 . 0.0)
          (cons 10 (list (- cx Ri) (+ by tubeStartDy)))       '(42 . 0.0)
          (cons 10 (list (+ cx Ri) (+ by tubeStartDy)))       '(42 . 0.0)
          (cons 10 (list (+ cx Ri) (+ by tubeStartDy tubeH))) '(42 . 0.0)
          (cons 10 (list (- cx Ri) (+ by tubeStartDy tubeH))) '(42 . 0.0)))

  ;; --- Ust flans dis ---
  (setvar "CLAYER" "ISKELE DETAY (BEYKENT)")
  (entmakex
    (list '(0 . "LWPOLYLINE") '(100 . "AcDbEntity")
          '(8 . "ISKELE DETAY (BEYKENT)")
          '(100 . "AcDbPolyline") '(90 . 4) '(70 . 1) '(43 . 0.0)
          (cons 10 (list (- cx flangeR) (+ by flangeStart)))  '(42 . 0.0)
          (cons 10 (list (+ cx flangeR) (+ by flangeStart)))  '(42 . 0.0)
          (cons 10 (list (+ cx flangeR) (+ by flangeEnd)))    '(42 . 0.0)
          (cons 10 (list (- cx flangeR) (+ by flangeEnd)))    '(42 . 0.0)))

  ;; --- Ust flans ic (gizli) ---
  (setvar "CLAYER" "ISKELE DETAY GIZLI (BEYKENT)")
  (entmakex
    (list '(0 . "LWPOLYLINE") '(100 . "AcDbEntity")
          '(8 . "ISKELE DETAY GIZLI (BEYKENT)")
          '(100 . "AcDbPolyline") '(90 . 4) '(70 . 1) '(43 . 0.0)
          (cons 10 (list (- cx flangeRi) (+ by flangeStart)))  '(42 . 0.0)
          (cons 10 (list (+ cx flangeRi) (+ by flangeStart)))  '(42 . 0.0)
          (cons 10 (list (+ cx flangeRi) (+ by flangeEnd)))    '(42 . 0.0)
          (cons 10 (list (- cx flangeRi) (+ by flangeEnd)))    '(42 . 0.0)))

  ;; --- Yatay baglanti plakalari (5 adet, 2.5 yuksek, 64.15 genis) ---
  (setvar "CLAYER" "ISKELE DETAY (BEYKENT)")
  (foreach py plateYs
    (entmakex
      (list '(0 . "LWPOLYLINE") '(100 . "AcDbEntity")
            '(8 . "ISKELE DETAY (BEYKENT)")
            '(100 . "AcDbPolyline") '(90 . 4) '(70 . 1) '(43 . 0.0)
            (cons 10 (list (- cx plateHW) (+ by py 1.25)))  '(42 . 0.0)
            (cons 10 (list (+ cx plateHW) (+ by py 1.25)))  '(42 . 0.0)
            (cons 10 (list (+ cx plateHW) (+ by py -1.25))) '(42 . 0.0)
            (cons 10 (list (- cx plateHW) (+ by py -1.25))) '(42 . 0.0)))
  )

  ;; --- Taban plakasi ve diz baglantisi (sadece D1) ---
  (if hasBase
    (progn
      ;; Taban plakasi (4 cizgi, +-45, dy 0-5)
      (vla-AddLine ms
        (vlax-3d-point (list (- cx 45.0) by 0.0))
        (vlax-3d-point (list (- cx 45.0) (+ by 5.0) 0.0)))
      (vla-AddLine ms
        (vlax-3d-point (list (+ cx 45.0) by 0.0))
        (vlax-3d-point (list (+ cx 45.0) (+ by 5.0) 0.0)))
      (vla-AddLine ms
        (vlax-3d-point (list (- cx 45.0) (+ by 5.0) 0.0))
        (vlax-3d-point (list (+ cx 45.0) (+ by 5.0) 0.0)))
      (vla-AddLine ms
        (vlax-3d-point (list (- cx 45.0) by 0.0))
        (vlax-3d-point (list (+ cx 45.0) by 0.0)))
      ;; Sag diz baglantisi
      (vla-AddLine ms
        (vlax-3d-point (list (+ cx 17.175) (+ by 5.0) 0.0))
        (vlax-3d-point (list (+ cx 12.175) (+ by 10.0) 0.0)))
      (vla-AddLine ms
        (vlax-3d-point (list (+ cx 22.175) (+ by 55.0) 0.0))
        (vlax-3d-point (list (+ cx 42.075) (+ by 20.0) 0.0)))
      (vla-AddLine ms
        (vlax-3d-point (list (+ cx 42.075) (+ by 20.0) 0.0))
        (vlax-3d-point (list (+ cx 42.075) (+ by 5.0) 0.0)))
      (vla-AddLine ms
        (vlax-3d-point (list (+ cx 12.175) (+ by 55.0) 0.0))
        (vlax-3d-point (list (+ cx 22.175) (+ by 55.0) 0.0)))
      (setvar "CLAYER" "ISKELE DETAY GIZLI (BEYKENT)")
      (vla-AddLine ms
        (vlax-3d-point (list (+ cx 42.075) (+ by 5.0) 0.0))
        (vlax-3d-point (list (+ cx 17.175) (+ by 5.0) 0.0)))
      ;; Sol diz baglantisi (ayna)
      (setvar "CLAYER" "ISKELE DETAY (BEYKENT)")
      (vla-AddLine ms
        (vlax-3d-point (list (- cx 17.175) (+ by 5.0) 0.0))
        (vlax-3d-point (list (- cx 12.175) (+ by 10.0) 0.0)))
      (vla-AddLine ms
        (vlax-3d-point (list (- cx 12.175) (+ by 55.0) 0.0))
        (vlax-3d-point (list (- cx 22.175) (+ by 55.0) 0.0)))
      (vla-AddLine ms
        (vlax-3d-point (list (- cx 22.175) (+ by 55.0) 0.0))
        (vlax-3d-point (list (- cx 42.075) (+ by 20.0) 0.0)))
      (vla-AddLine ms
        (vlax-3d-point (list (- cx 42.075) (+ by 20.0) 0.0))
        (vlax-3d-point (list (- cx 42.075) (+ by 5.0) 0.0)))
      (setvar "CLAYER" "ISKELE DETAY GIZLI (BEYKENT)")
      (vla-AddLine ms
        (vlax-3d-point (list (- cx 42.075) (+ by 5.0) 0.0))
        (vlax-3d-point (list (- cx 17.175) (+ by 5.0) 0.0)))
    )
  )

  ;; --- Boyut: toplam yukseklik ---
  (setvar "CLAYER" "OLCU (BEYKENT)")
  (command "_.-DIMSTYLE" "_R" "PLAN_OLCU_DETAY")
  (setq dimObj (vl-catch-all-apply 'vla-AddDimAligned
    (list ms
      (vlax-3d-point (list (+ cx R 5.0) by 0.0))
      (vlax-3d-point (list (+ cx R 5.0) (+ by totalH) 0.0))
      (vlax-3d-point (list (+ cx R 45.0) (+ by (* totalH 0.5)) 0.0)))))
  (if (not (vl-catch-all-error-p dimObj))
    (vla-put-TextOverride dimObj (rtos (* totalH 2.0) 2 0)))

  ;; --- Boyut: boru capi ---
  (setq dimObj (vl-catch-all-apply 'vla-AddDimAligned
    (list ms
      (vlax-3d-point (list (- cx R) (+ by (* totalH 0.6)) 0.0))
      (vlax-3d-point (list (+ cx R) (+ by (* totalH 0.6)) 0.0))
      (vlax-3d-point (list cx (+ by (* totalH 0.6) 15.0) 0.0)))))
  (if (not (vl-catch-all-error-p dimObj))
    (vla-put-TextOverride dimObj "48.3"))

  ;; --- Etiket ---
  (setvar "CLAYER" "YAZI (BEYKENT)")
  (setq txtObj (vla-AddText ms
    (strcat label " (D48.3/3.2)")
    (vlax-3d-point (list (- cx R 14.0) (+ by 82.0) 0.0))
    15.0))
  (vla-put-Rotation txtObj (* 90.0 (/ pi 180.0)))
  (vl-catch-all-apply 'vla-put-StyleName (list txtObj "YAZI (BEYKENT)"))

  (setvar "CLAYER" curLay)
)

;;; ============ CAPRAZ DETAY (C1, 1:10 OLCEK) ============

(defun _isk:draw-capraz-detay (ms basePt label /
  cx by R Ri curLay totalH tubeBot tubeTop
  forkR arcR boltR arcCenDx arcCenDy
  dimObj txtObj)

  (setq cx (car basePt) by (cadr basePt))
  (setq R 12.075 Ri 10.825)
  (setq totalH 1767.77)
  (setq tubeBot 46.43 tubeTop (- totalH 46.43))
  (setq forkR 14.808 arcR 8.244 boltR 6.315)
  (setq arcCenDx 6.566 arcCenDy 40.29)
  (setq curLay (getvar "CLAYER"))

  ;; --- Dis boru cizgileri (2 dikey, CAPRAZ katmani) ---
  (setvar "CLAYER" "ISKELE DETAY (BEYKENT)")
  (vla-AddLine ms
    (vlax-3d-point (list (- cx R) (+ by tubeBot) 0.0))
    (vlax-3d-point (list (- cx R) (+ by tubeTop) 0.0)))
  (vla-AddLine ms
    (vlax-3d-point (list (+ cx R) (+ by tubeBot) 0.0))
    (vlax-3d-point (list (+ cx R) (+ by tubeTop) 0.0)))

  ;; --- Ic boru cizgileri (gizli) ---
  (setvar "CLAYER" "ISKELE DETAY GIZLI (BEYKENT)")
  (vla-AddLine ms
    (vlax-3d-point (list (- cx Ri) (+ by tubeBot) 0.0))
    (vlax-3d-point (list (- cx Ri) (+ by tubeTop) 0.0)))
  (vla-AddLine ms
    (vlax-3d-point (list (+ cx Ri) (+ by tubeBot) 0.0))
    (vlax-3d-point (list (+ cx Ri) (+ by tubeTop) 0.0)))

  ;; --- Yatay baglanti cubugu (ust ve alt) ---
  (setvar "CLAYER" "ISKELE DETAY (BEYKENT)")
  (vla-AddLine ms
    (vlax-3d-point (list (- cx R) (+ by tubeBot) 0.0))
    (vlax-3d-point (list (+ cx R) (+ by tubeBot) 0.0)))
  (vla-AddLine ms
    (vlax-3d-point (list (- cx R) (+ by tubeTop) 0.0))
    (vlax-3d-point (list (+ cx R) (+ by tubeTop) 0.0)))

  ;; --- Alt catal baglantisi (foot) ---
  ;; Sag dikey
  (vla-AddLine ms
    (vlax-3d-point (list (+ cx forkR) (+ by arcCenDy) 0.0))
    (vlax-3d-point (list (+ cx forkR) by 0.0)))
  ;; Sol dikey
  (vla-AddLine ms
    (vlax-3d-point (list (- cx forkR) by 0.0))
    (vlax-3d-point (list (- cx forkR) (+ by arcCenDy) 0.0)))
  ;; Yarim daire (alt)
  (vla-AddArc ms
    (vlax-3d-point (list cx by 0.0))
    forkR pi 0.0)
  ;; Civata dairesi (alt)
  (vla-AddCircle ms (vlax-3d-point (list cx by 0.0)) boltR)
  ;; Kucuk yaylar (boru-catal gecisi)
  (vla-AddArc ms
    (vlax-3d-point (list (+ cx arcCenDx) (+ by arcCenDy) 0.0))
    arcR (* 1.0 (/ pi 180.0)) (* 48.0 (/ pi 180.0)))
  (vla-AddArc ms
    (vlax-3d-point (list (- cx arcCenDx) (+ by arcCenDy) 0.0))
    arcR (* 132.0 (/ pi 180.0)) (* 179.0 (/ pi 180.0)))

  ;; --- Ust catal baglantisi (head, ayna) ---
  ;; Sag dikey
  (vla-AddLine ms
    (vlax-3d-point (list (+ cx forkR) (+ by (- totalH arcCenDy)) 0.0))
    (vlax-3d-point (list (+ cx forkR) (+ by totalH) 0.0)))
  ;; Sol dikey
  (vla-AddLine ms
    (vlax-3d-point (list (- cx forkR) (+ by totalH) 0.0))
    (vlax-3d-point (list (- cx forkR) (+ by (- totalH arcCenDy)) 0.0)))
  ;; Yarim daire (ust)
  (vla-AddArc ms
    (vlax-3d-point (list cx (+ by totalH) 0.0))
    forkR 0.0 pi)
  ;; Civata dairesi (ust)
  (vla-AddCircle ms (vlax-3d-point (list cx (+ by totalH) 0.0)) boltR)
  ;; Kucuk yaylar (ust, ters acilar)
  (vla-AddArc ms
    (vlax-3d-point (list (+ cx arcCenDx) (+ by (- totalH arcCenDy)) 0.0))
    arcR (* 312.0 (/ pi 180.0)) (* 359.0 (/ pi 180.0)))
  (vla-AddArc ms
    (vlax-3d-point (list (- cx arcCenDx) (+ by (- totalH arcCenDy)) 0.0))
    arcR (* 181.0 (/ pi 180.0)) (* 228.0 (/ pi 180.0)))

  ;; --- Boyut: toplam yukseklik ---
  (setvar "CLAYER" "OLCU (BEYKENT)")
  (command "_.-DIMSTYLE" "_R" "PLAN_OLCU_DETAY")
  (setq dimObj (vl-catch-all-apply 'vla-AddDimAligned
    (list ms
      (vlax-3d-point (list (+ cx R 5.0) by 0.0))
      (vlax-3d-point (list (+ cx R 5.0) (+ by totalH) 0.0))
      (vlax-3d-point (list (+ cx R 45.0) (+ by (* totalH 0.5)) 0.0)))))
  (if (not (vl-catch-all-error-p dimObj))
    (vla-put-TextOverride dimObj (rtos (* totalH 2.0) 2 0)))

  ;; --- Boyut: boru capi ---
  (setq dimObj (vl-catch-all-apply 'vla-AddDimAligned
    (list ms
      (vlax-3d-point (list (- cx R) (+ by (* totalH 0.5)) 0.0))
      (vlax-3d-point (list (+ cx R) (+ by (* totalH 0.5)) 0.0))
      (vlax-3d-point (list cx (+ by (* totalH 0.5) 15.0) 0.0)))))
  (if (not (vl-catch-all-error-p dimObj))
    (vla-put-TextOverride dimObj "48.3"))

  ;; --- Etiket ---
  (setvar "CLAYER" "YAZI (BEYKENT)")
  (setq txtObj (vla-AddText ms
    (strcat label " (D48.3/3.2)")
    (vlax-3d-point (list (- cx R 14.0) (+ by 82.0) 0.0))
    15.0))
  (vla-put-Rotation txtObj (* 90.0 (/ pi 180.0)))
  (vl-catch-all-apply 'vla-put-StyleName (list txtObj "YAZI (BEYKENT)"))

  (setvar "CLAYER" curLay)
)

;;; ============ KATMAN / STIL ISLEMLERI (VLA) ============

(defun _isk:make-layer (lname color ltype lw / layers obj)
  (if (and (not (equal (strcase ltype) "CONTINUOUS"))
           (not (tblsearch "LTYPE" ltype)))
    (progn
      (command "_.-LINETYPE" "_L" ltype "acadiso.lin" "")
      (if (not (tblsearch "LTYPE" ltype))
        (setq ltype "Continuous"))
    )
  )
  (setq layers (vla-get-Layers
    (vla-get-ActiveDocument (vlax-get-acad-object))))
  (if (vl-catch-all-error-p
        (vl-catch-all-apply 'vla-Item (list layers lname)))
    (setq obj (vla-Add layers lname))
    (setq obj (vla-Item layers lname))
  )
  (vl-catch-all-apply 'vla-put-Lock (list obj :vlax-false))
  (vl-catch-all-apply 'vla-put-Color (list obj color))
  (if (tblsearch "LTYPE" ltype)
    (vl-catch-all-apply 'vla-put-Linetype (list obj ltype)))
  (if lw (vl-catch-all-apply 'vla-put-Lineweight (list obj lw)))
)

(defun _isk:make-text-style (sname fontName / styles obj)
  (setq styles (vla-get-TextStyles
    (vla-get-ActiveDocument (vlax-get-acad-object))))
  (if (vl-catch-all-error-p
        (vl-catch-all-apply 'vla-Item (list styles sname)))
    (progn
      (setq obj (vla-Add styles sname))
      (vl-catch-all-apply 'vla-SetFont
        (list obj fontName :vlax-false :vlax-false 0 34))
    )
  )
)

(defun _isk:make-dim-style (sname settings / key val)
  (foreach pair settings
    (setq key (car pair) val (cdr pair))
    (vl-catch-all-apply 'setvar (list key val))
  )
  (if (tblsearch "DIMSTYLE" sname)
    (command "_.-DIMSTYLE" "_S" sname "_Y")
    (command "_.-DIMSTYLE" "_S" sname)
  )
)

;;; ============ OFFSET ============

(defun _isk:offset-outside (ename dist outsidePt / last newe)
  (setq last (entlast))
  (command "_.OFFSET" dist ename outsidePt "")
  (setq newe (entlast))
  (if (eq last newe) nil newe)
)

;;; ============ ANA KOMUT ============

(defun c:ISKELEPLAN ( / en vlaObj dFar bay oldLay
                      pts cen pOn dir outPt
                      eFar outerPts
                      acad doc ms
                      p hitPts pOut hitCnt
                      innerDikmePts outerDikmePts dVal sortedDists totalLen totalLenOut
                      j d1 d2 gap dd lastGap newPt nrm outerPt
                      subCnt sortedInner sortedOuter k
                      yataySegs segLen roundLen otherCats labelMap
                      nextYNum yNum pair midPt ang labelStr txtObj txtPt
                      enBuild buildPts ankCnt closePt perp
                      ankCandidates segIdx segCnt segCounters
                      prevSkipped shouldDraw cand
                      detayBase detayXOff detayYNum detPt
                      allVertexPts dimObj dimLinePt outerSA)

  (vl-load-com)

  ;; 1) Polyline sec (iskele referans poligonu)
  (prompt "\nKapali referans polyline secin (iskele poligonu): ")
  (setq en (car (entsel)))
  (if (null en)
    (progn (prompt "\nSecim yapilmadi.") (princ) (exit))
  )

  ;; 1b) Ic polyline sec (bina / doseme siniri - ankraj icin)
  (prompt "\nIc polyline secin (bina siniri - ankraj icin): ")
  (setq enBuild (car (entsel)))
  (if (null enBuild)
    (progn (prompt "\nSecim yapilmadi.") (princ) (exit))
  )

  (setq vlaObj (vlax-ename->vla-object en))
  (setq acad (vlax-get-acad-object))
  (setq doc  (vla-get-ActiveDocument acad))
  (setq ms   (vla-get-ModelSpace doc))

  (if (not (vlax-get-property vlaObj 'Closed))
    (progn (prompt "\nPolyline kapali degil!") (princ) (exit))
  )

  ;; Sabitler
  (setq dFar 70.0)
  (setq bay 250.0)

  ;; 2) Ic poligon verteksleri (dogrusal olanlar temizlenir)
  (setq pts (_isk:lwpoly-pts en))
  (if (null pts)
    (progn (prompt "\nVertex bulunamadi!") (princ) (exit))
  )
  (prompt (strcat "\nOrijinal vertex: " (itoa (length pts))))
  (setq pts (_isk:remove-collinear pts))
  (prompt (strcat "\nTemizlenmis vertex: " (itoa (length pts))))
  (setq cen (_isk:centroid-avg pts))

  ;; Dis yonu bul (signed area ile - konkav poligonlarda da dogru calisir)
  (setq pOn (car pts))
  (setq dir (_isk:v- (cadr pts) pOn))
  ;; Sol normal (-dy, dx)
  (setq nrm (_isk:vunit (list (- (cadr dir)) (car dir) 0.0)))
  ;; CCW (sa>0): sol normal iceri -> flip  /  CW (sa<0): sol normal disa -> kalsinn
  (if (> (_isk:signed-area pts) 0.0) (setq nrm (_isk:v* nrm -1.0)))
  (setq outPt (_isk:v+ pOn (_isk:v* nrm 1000.0)))

  ;; 3) ByLayer ayarlari: tum cizimler katman rengini, cizgi tipini ve kalinligini kullansin
  (setq oldLay   (getvar "CLAYER"))
  (setq _isk:oldCecolor  (getvar "CECOLOR"))
  (setq _isk:oldCelweight (getvar "CELWEIGHT"))
  (setq _isk:oldCeltype  (getvar "CELTYPE"))
  (setvar "CECOLOR" "256")
  (setvar "CELWEIGHT" -1)
  (setvar "CELTYPE" "ByLayer")
  (_isk:make-layer "YAZI (BEYKENT)"                  4   "Continuous"       20)
  (_isk:make-layer "ISKELE DETAY (BEYKENT)"           4   "Continuous"       25)
  (_isk:make-layer "OLCU (BEYKENT)"                  14   "Continuous"       20)
  (_isk:make-layer "ISKELE CAPRAZ (BEYKENT)"        140   "Continuous"       20)
  (_isk:make-layer "ISKELE DETAY GIZLI (BEYKENT)"     8   "HIDDEN2"          15)
  (_isk:make-layer "ISKELE FLANS DETAY (BEYKENT)"     3   "Continuous"       20)
  (_isk:make-layer "ISKELE FLANS (BEYKENT)"         160   "Continuous"       20)
  (_isk:make-layer "ISKELE YATAY (BEYKENT)"         210   "Continuous"       30)
  (_isk:make-layer "ISKELE BOLT (BEYKENT)"            5   "Continuous"       20)
  (_isk:make-layer "ISKELE ANKRAJ (BEYKENT)"         95   "Continuous"       20)
  (_isk:make-layer "DOSEME SINIRI (BEYKENT)"         71   "Continuous"       30)

  ;; Yazi stili
  (_isk:make-text-style "YAZI (BEYKENT)" "Bahnschrift Light Condensed")

  ;; Olcu stilleri
  (_isk:make-dim-style "PLAN_OLCU"
    '(("DIMTXSTY" . "YAZI (BEYKENT)") ("DIMASZ" . 3.0) ("DIMEXO" . 3.0)
      ("DIMDLE" . 0.5) ("DIMTIH" . 0) ("DIMTOH" . 0) ("DIMTAD" . 1)
      ("DIMZIN" . 12) ("DIMTXT" . 12.0) ("DIMTSZ" . 3.0) ("DIMGAP" . 2.0)
      ("DIMTOFL" . 1) ("DIMTIX" . 1) ("DIMCLRT" . 7) ("DIMCLRD" . 256)
      ("DIMCLRE" . 256) ("DIMDEC" . 0) ("DIMLFAC" . 1.0)))
  (_isk:make-dim-style "PLAN_OLCU_DETAY"
    '(("DIMTXSTY" . "YAZI (BEYKENT)") ("DIMASZ" . 6.0) ("DIMEXO" . 3.0)
      ("DIMTIH" . 0) ("DIMTOH" . 0) ("DIMTAD" . 1) ("DIMZIN" . 12)
      ("DIMTXT" . 12.0) ("DIMLFAC" . 2.0) ("DIMGAP" . 2.0)
      ("DIMTOFL" . 1) ("DIMSAH" . 1) ("DIMTIX" . 1) ("DIMCLRT" . 7)
      ("DIMCLRD" . 256) ("DIMCLRE" . 256) ("DIMDEC" . 1) ("DIMTDEC" . 1)))

  ;; 4) 70 cm offset
  (setvar "CLAYER" "ISKELE YATAY (BEYKENT)")
  (setq eFar (_isk:offset-outside en dFar outPt))
  (if (null eFar)
    (progn (setvar "CLAYER" oldLay) (prompt "\nOffset basarisiz!") (princ) (exit))
  )

  ;; 5) Dis poligon verteksleri
  (setq outerPts (_isk:lwpoly-pts eFar))
  (setq outerSA (_isk:signed-area outerPts))
  (prompt (strcat "\nDis vertex: " (itoa (length outerPts))))

  ;; 6) Vertex dikmeleri + teget dikmeleri
  (setq hitCnt 0)

  ;; Dikme sembolleri -> ISKELE_DIKME
  (setvar "CLAYER" "ISKELE FLANS (BEYKENT)")
  (foreach p pts      (_isk:draw-dikme ms p))
  (foreach p outerPts (_isk:draw-dikme ms p))

  ;; Vertex noktalarinin birlesik listesi (yakinlik kontrolu icin)
  (setq allVertexPts (append pts outerPts))

  ;; IC vertekslerden: teget noktalara
  ;; (20cm yakin + bossluk<=250 ise atla, yoksa kalsin)
  (foreach p pts
    (setq hitPts (_isk:circle-poly-tangents p dFar outerPts))
    (foreach pOut hitPts
      (if (not (_isk:can-skip-tangent pOut outerPts allVertexPts))
        (progn
          (setvar "CLAYER" "ISKELE YATAY (BEYKENT)")
          (_isk:draw-yatay ms p pOut)
          (setvar "CLAYER" "ISKELE FLANS (BEYKENT)")
          (_isk:draw-dikme ms pOut)
          (setq hitCnt (1+ hitCnt))
        )
      )
    )
    (setq hitPts (_isk:circle-poly-tangents p dFar pts))
    (foreach pOut hitPts
      (if (and (> (distance p pOut) 5.0)
               (not (_isk:can-skip-tangent pOut pts allVertexPts)))
        (progn
          (setvar "CLAYER" "ISKELE YATAY (BEYKENT)")
          (_isk:draw-yatay ms p pOut)
          (setvar "CLAYER" "ISKELE FLANS (BEYKENT)")
          (_isk:draw-dikme ms pOut)
          (setq hitCnt (1+ hitCnt))
        )
      )
    )
  )

  ;; DIS vertekslerden: teget noktalara
  (foreach p outerPts
    (setq hitPts (_isk:circle-poly-tangents p dFar pts))
    (foreach pOut hitPts
      (if (not (_isk:can-skip-tangent pOut pts allVertexPts))
        (progn
          (setvar "CLAYER" "ISKELE YATAY (BEYKENT)")
          (_isk:draw-yatay ms p pOut)
          (setvar "CLAYER" "ISKELE FLANS (BEYKENT)")
          (_isk:draw-dikme ms pOut)
          (setq hitCnt (1+ hitCnt))
        )
      )
    )
    (setq hitPts (_isk:circle-poly-tangents p dFar outerPts))
    (foreach pOut hitPts
      (if (and (> (distance p pOut) 5.0)
               (not (_isk:can-skip-tangent pOut outerPts allVertexPts)))
        (progn
          (setvar "CLAYER" "ISKELE YATAY (BEYKENT)")
          (_isk:draw-yatay ms p pOut)
          (setvar "CLAYER" "ISKELE FLANS (BEYKENT)")
          (_isk:draw-dikme ms pOut)
          (setq hitCnt (1+ hitCnt))
        )
      )
    )
  )

  ;; 7) IC ve DIS poligon uzerindeki tum dikme noktalarini topla
  (setq innerDikmePts nil)
  (setq outerDikmePts nil)

  ;; Tum ic verteksler
  (foreach p pts (setq innerDikmePts (cons p innerDikmePts)))
  ;; Tum dis verteksler
  (foreach p outerPts (setq outerDikmePts (cons p outerDikmePts)))

  ;; Dis vertekslerden ic poligona dusen teget noktalar -> innerDikmePts
  ;; (20cm yakin + bossluk<=250 ise atla, yoksa kalsin)
  (foreach p outerPts
    (setq hitPts (_isk:circle-poly-tangents p dFar pts))
    (foreach pOut hitPts
      (if (not (_isk:can-skip-tangent pOut pts allVertexPts))
        (setq innerDikmePts (cons pOut innerDikmePts))
      )
    )
  )
  ;; Ic vertekslerden dis poligona dusen teget noktalar -> outerDikmePts
  (foreach p pts
    (setq hitPts (_isk:circle-poly-tangents p dFar outerPts))
    (foreach pOut hitPts
      (if (not (_isk:can-skip-tangent pOut outerPts allVertexPts))
        (setq outerDikmePts (cons pOut outerDikmePts))
      )
    )
  )

  ;; Mesafeye gore sirala (saat yonu = polyline yonu)
  (setq sortedDists nil)
  (foreach dp innerDikmePts
    (setq dVal (_isk:dist-along-poly dp pts))
    (setq sortedDists (cons (list dVal dp) sortedDists))
  )
  (setq sortedDists (vl-sort sortedDists '(lambda (a b) (< (car a) (car b)))))
  (setq totalLen (_isk:total-poly-len pts))

  ;; 8) Ardisik ana dikmeler arasi 250 cm'den buyukse ara dikme ekle
  (setq subCnt 0)
  (setq j 0)
  (while (< j (length sortedDists))
    (setq d1 (car (nth j sortedDists)))
    (if (< (1+ j) (length sortedDists))
      (setq d2 (car (nth (1+ j) sortedDists)))
      (setq d2 (+ (car (nth 0 sortedDists)) totalLen))
    )
    (setq gap (- d2 d1))
    (if (> gap (+ bay 1.0))
      (progn
        (setq dd (+ d1 bay))
        (while (< dd (- d2 1.0))
          ;; Son parcayi kontrol et: dd'den d2'ye kalan mesafe
          (setq lastGap (- d2 dd))
          (if (< lastGap 70.0)
            ;; Son parca 70'den kucuk: bu dikmeyi d2 - 70 konumuna cek
            (progn
              (setq dd (- d2 70.0))
              (if (<= dd d1) (setq dd (+ dd 0.0)))
            )
          )
          (setq newPt (_isk:pt-at-dist-on-poly (rem dd totalLen) pts))
          (setq nrm (_isk:outward-normal-at newPt pts cen))
          (setq outerPt (_isk:v+ newPt (_isk:v* nrm dFar)))
          (setvar "CLAYER" "ISKELE FLANS (BEYKENT)")
          (_isk:draw-dikme ms newPt)
          (_isk:draw-dikme ms outerPt)
          (setvar "CLAYER" "ISKELE YATAY (BEYKENT)")
          (_isk:draw-yatay ms newPt outerPt)
          ;; Ara dikme noktalarini da listele
          (setq innerDikmePts (cons newPt innerDikmePts))
          (setq outerDikmePts (cons outerPt outerDikmePts))
          (setq subCnt (1+ subCnt))
          ;; Son parcayi cekmisssek donguyu bitir
          (if (< lastGap 70.0)
            (setq dd (+ d2 1.0))
            (setq dd (+ dd bay))
          )
        )
      )
    )
    (setq j (1+ j))
  )

  ;; 9) Ardisik dikme noktalari arasina yatay baglanti cizgileri
  ;;    IC poligon boyunca
  (setq sortedInner nil)
  (foreach dp innerDikmePts
    (setq dVal (_isk:dist-along-poly dp pts))
    (setq sortedInner (cons (list dVal dp) sortedInner))
  )
  (setq sortedInner (vl-sort sortedInner '(lambda (a b) (< (car a) (car b)))))

  (setvar "CLAYER" "ISKELE YATAY (BEYKENT)")
  (setq k 0)
  (while (< k (1- (length sortedInner)))
    (setq p    (cadr (nth k sortedInner)))
    (setq pOut (cadr (nth (1+ k) sortedInner)))
    (_isk:draw-yatay ms p pOut)
    (setq k (1+ k))
  )
  ;; Son noktadan ilk noktaya (kapali poligon)
  (if (> (length sortedInner) 1)
    (progn
      (setq p    (cadr (nth (1- (length sortedInner)) sortedInner)))
      (setq pOut (cadr (nth 0 sortedInner)))
      (_isk:draw-yatay ms p pOut)
    )
  )

  ;;    DIS poligon boyunca
  (setq sortedOuter nil)
  (setq totalLenOut (_isk:total-poly-len outerPts))
  (foreach dp outerDikmePts
    (setq dVal (_isk:dist-along-poly dp outerPts))
    (setq sortedOuter (cons (list dVal dp) sortedOuter))
  )
  (setq sortedOuter (vl-sort sortedOuter '(lambda (a b) (< (car a) (car b)))))

  (setq k 0)
  (while (< k (1- (length sortedOuter)))
    (setq p    (cadr (nth k sortedOuter)))
    (setq pOut (cadr (nth (1+ k) sortedOuter)))
    (_isk:draw-yatay ms p pOut)
    (setq k (1+ k))
  )
  ;; Son noktadan ilk noktaya (kapali poligon)
  (if (> (length sortedOuter) 1)
    (progn
      (setq p    (cadr (nth (1- (length sortedOuter)) sortedOuter)))
      (setq pOut (cadr (nth 0 sortedOuter)))
      (_isk:draw-yatay ms p pOut)
    )
  )

  ;; 10) Dis poligon yatay etiketleri
  ;;     Format: Y<no> (D48.3/3.2)
  ;;     70cm -> Y1, 250cm -> Y2, diger uzunluklar -> Y3, Y4, ...
  (setvar "CLAYER" "YAZI (BEYKENT)")

  ;; Segment bilgilerini topla (sortedOuter kullanarak)
  (setq yataySegs nil)
  (setq k 0)
  (while (< k (length sortedOuter))
    (if (< (1+ k) (length sortedOuter))
      (setq p    (cadr (nth k sortedOuter))
            pOut (cadr (nth (1+ k) sortedOuter)))
      (setq p    (cadr (nth k sortedOuter))
            pOut (cadr (nth 0 sortedOuter)))
    )
    (setq segLen (distance p pOut))
    (setq yataySegs (cons (list p pOut segLen) yataySegs))
    (setq k (1+ k))
  )
  (setq yataySegs (reverse yataySegs))

  ;; Unique uzunluk kategorileri (70 ve 250 haric)
  (setq otherCats nil)
  (foreach seg yataySegs
    (setq segLen (caddr seg))
    (setq roundLen (fix (+ segLen 0.5)))
    (if (and (or (< segLen 65.0) (> segLen 75.0))
             (or (< segLen 245.0) (> segLen 255.0)))
      (if (not (member roundLen otherCats))
        (setq otherCats (cons roundLen otherCats))
      )
    )
  )
  (setq otherCats (vl-sort otherCats '<))

  ;; Numara atama: 70->1, 250->2, diger uzunluklar sirali 3,4,...
  (setq labelMap nil nextYNum 3)
  (foreach c otherCats
    (setq labelMap (cons (cons c nextYNum) labelMap))
    (setq nextYNum (1+ nextYNum))
  )

  ;; Her segmente etiket yerlestir
  (foreach seg yataySegs
    (setq p    (car seg)
          pOut (cadr seg)
          segLen (caddr seg))
    (setq roundLen (fix (+ segLen 0.5)))
    (cond
      ((and (>= segLen 65.0) (<= segLen 75.0)) (setq yNum 1))
      ((and (>= segLen 245.0) (<= segLen 255.0)) (setq yNum 2))
      (T (setq pair (assoc roundLen labelMap))
         (setq yNum (if pair (cdr pair) 0)))
    )
    ;; Segmentin orta noktasi
    (setq midPt (list (/ (+ (car p) (car pOut)) 2.0)
                      (/ (+ (cadr p) (cadr pOut)) 2.0)
                      0.0))
    (setq dir (_isk:vunit (_isk:v- pOut p)))
    (setq perp (list (- (cadr dir)) (car dir) 0.0))
    ;; Dis yone dogru (signed area ile: CCW ise flip, CW ise kalsin)
    (if (> outerSA 0.0) (setq perp (_isk:v* perp -1.0)))
    ;; Metin konumu: yatay elemanlarin disinda (2.5cm yatay + 4cm yazi/2 + 3.5cm bosluk)
    (setq txtPt (_isk:v+ midPt (_isk:v* perp 10.0)))
    ;; Metin acisi: segment yonunde, okunabilir olacak sekilde
    (setq ang (atan (cadr dir) (car dir)))
    ;; Okunabilirlik: aci -90..+90 derece arasinda olmali (sag veya alttan okunur)
    (if (or (> ang (/ pi 2.0)) (< ang (/ pi -2.0)))
      (setq ang (+ ang pi))
    )
    (setq labelStr (strcat "Y" (itoa yNum) " (D48.3/3.2)"))
    (setq txtObj (vla-AddText ms labelStr (vlax-3d-point '(0 0 0)) 8.0))
    (vla-put-Alignment txtObj 10)
    (vla-put-TextAlignmentPoint txtObj (vlax-3d-point txtPt))
    (vla-put-Rotation txtObj ang)
    (vl-catch-all-apply 'vla-put-StyleName (list txtObj "YAZI (BEYKENT)"))
  )

  ;; 10b) Dis poligondaki dikme merkezleri arasina olcu at (40cm disarida)
  (setvar "CLAYER" "OLCU (BEYKENT)")
  (command "_.-DIMSTYLE" "_R" "PLAN_OLCU")
  (setq k 0)
  (while (< k (length sortedOuter))
    (if (< (1+ k) (length sortedOuter))
      (setq p    (cadr (nth k sortedOuter))
            pOut (cadr (nth (1+ k) sortedOuter)))
      (setq p    (cadr (nth k sortedOuter))
            pOut (cadr (nth 0 sortedOuter)))
    )
    (setq midPt (list (/ (+ (car p) (car pOut)) 2.0)
                      (/ (+ (cadr p) (cadr pOut)) 2.0) 0.0))
    (setq dir (_isk:vunit (_isk:v- pOut p)))
    (setq perp (list (- (cadr dir)) (car dir) 0.0))
    ;; Dis yone dogru (signed area ile)
    (if (> outerSA 0.0) (setq perp (_isk:v* perp -1.0)))
    (setq dimLinePt (_isk:v+ midPt (_isk:v* perp 40.0)))
    (setq dimObj (vl-catch-all-apply 'vla-AddDimAligned
      (list ms (vlax-3d-point p) (vlax-3d-point pOut)
            (vlax-3d-point dimLinePt))))
    (if (not (vl-catch-all-error-p dimObj))
      (vl-catch-all-apply 'vla-put-StyleName (list dimObj "PLAN_OLCU"))
    )
    (setq k (1+ k))
  )

  ;; 11) Ankraj: bir dolu bir bos (ayni segmentte), iki bos ardarda gelmesin
  (setq buildPts (_isk:lwpoly-pts enBuild))
  (if buildPts
    (progn
      (setvar "CLAYER" "ISKELE ANKRAJ (BEYKENT)")
      (setq ankCnt 0)

      ;; Adaylari topla (sirali - sortedInner kullanarak)
      (setq ankCandidates nil)
      (setq k 0)
      (while (< k (length sortedInner))
        (setq p (cadr (nth k sortedInner)))
        (setq closePt (_isk:closest-perp-on-poly p buildPts))
        (if (and closePt
                 (> (distance p closePt) 8.0)
                 (<= (distance p closePt) 45.0))
          (progn
            (setq segIdx (_isk:find-seg-idx closePt buildPts))
            (setq ankCandidates
              (append ankCandidates
                (list (list p closePt segIdx))))
          )
        )
        (setq k (1+ k))
      )

      ;; Tum gecerli ankrajlari ciz
      (foreach cand ankCandidates
        (setq p (car cand) closePt (cadr cand))
        (_isk:draw-ankraj ms p closePt)
        ;; Etiket: plakaya paralel, ic poligonun icinde
        (setq dir (_isk:vunit (_isk:v- closePt p)))
        (setq perp (list (- (cadr dir)) (car dir) 0.0))
        (setq txtPt (_isk:v+ closePt (_isk:v* dir 8.0)))
        (setq ang (atan (cadr perp) (car perp)))
        (if (or (> ang (/ pi 2.0)) (< ang (/ pi -2.0)))
          (setq ang (+ ang pi))
        )
        (setq txtObj (vla-AddText ms "ANKRAJ"
                       (vlax-3d-point '(0 0 0)) 8.0))
        (vla-put-Alignment txtObj 10)
        (vla-put-TextAlignmentPoint txtObj (vlax-3d-point txtPt))
        (vla-put-Rotation txtObj ang)
        (vl-catch-all-apply 'vla-put-StyleName (list txtObj "YAZI (BEYKENT)"))
        (vl-catch-all-apply 'vla-put-Layer (list txtObj "YAZI (BEYKENT)"))
        (setq ankCnt (1+ ankCnt))
      )
      (prompt (strcat "\nAnkraj: " (itoa ankCnt)))
    )
  )

  ;; 12) Detay cizimleri: D1, D2, C1, Y1, Y2, Y3... (1:10 olcek = 5x buyuk)
  (prompt "\nDetay cizimi icin baslangic noktasi secin (veya ESC): ")
  (setq detayBase (getpoint))
  (if detayBase
    (progn
      (setq detayBase (list (car detayBase) (cadr detayBase) 0.0))
      (setq detayXOff 0.0)

      ;; D1: Dikme detay (tubeH=1370, taban plakali)
      (setq detPt (list (+ (car detayBase) detayXOff) (cadr detayBase) 0.0))
      (_isk:draw-dikme-detay ms detPt 1370.0 5.0
        '(250.0 500.0 750.0 1000.0 1250.0) T "D1")
      (setq detayXOff (+ detayXOff 250.0))

      ;; D2: Dikme detay (tubeH=1250, tabansiz)
      (setq detPt (list (+ (car detayBase) detayXOff) (cadr detayBase) 0.0))
      (_isk:draw-dikme-detay ms detPt 1250.0 0.0
        '(125.0 375.0 625.0 875.0 1125.0) nil "D2")
      (setq detayXOff (+ detayXOff 230.0))

      ;; C1: Capraz detay
      (setq detPt (list (+ (car detayBase) detayXOff) (cadr detayBase) 0.0))
      (_isk:draw-capraz-detay ms detPt "C1")
      (setq detayXOff (+ detayXOff 200.0))

      ;; Y1: 70 cm standart yatay
      (setq detPt (list (+ (car detayBase) detayXOff) (cadr detayBase) 0.0))
      (_isk:draw-yatay-detay ms detPt 70.0 "Y1")
      (setq detayXOff (+ detayXOff 160.0))

      ;; Y2: 250 cm standart yatay
      (setq detPt (list (+ (car detayBase) detayXOff) (cadr detayBase) 0.0))
      (_isk:draw-yatay-detay ms detPt 250.0 "Y2")
      (setq detayXOff (+ detayXOff 160.0))

      ;; Y3+: standart disi uzunluklar
      (setq detayYNum 3)
      (foreach roundLen otherCats
        (setq detPt (list (+ (car detayBase) detayXOff) (cadr detayBase) 0.0))
        (_isk:draw-yatay-detay ms detPt roundLen
          (strcat "Y" (itoa detayYNum)))
        (setq detayXOff (+ detayXOff 160.0))
        (setq detayYNum (1+ detayYNum))
      )
      (prompt (strcat "\nDetay: D1, D2, C1 + "
                      (itoa (+ 2 (length otherCats)))
                      " adet yatay eleman (Y1-Y"
                      (itoa (+ 2 (length otherCats))) ") cizildi."))
    )
  )

  ;; Referans poligonlari sil (1. ve 2. poligon)
  (if en   (entdel en))
  (if eFar (entdel eFar))

  (setvar "CLAYER" oldLay)
  (if _isk:oldCecolor  (setvar "CECOLOR"  _isk:oldCecolor))
  (if _isk:oldCelweight (setvar "CELWEIGHT" _isk:oldCelweight))
  (if _isk:oldCeltype  (setvar "CELTYPE"  _isk:oldCeltype))
  (prompt (strcat "\n\nTamamlandi. Ana dikme: "
                  (itoa (+ (length pts) (length outerPts) hitCnt))
                  " | Ara dikme (250): " (itoa subCnt)
                  " | Ankraj: " (itoa (if ankCnt ankCnt 0))))
  (princ)
)

(prompt "\nYuklendi: ISKELEPLAN komutunu calistirin.")
(princ)
